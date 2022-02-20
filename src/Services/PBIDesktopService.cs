﻿namespace Sqlbi.Bravo.Services
{
    using Microsoft.AnalysisServices;
    using Sqlbi.Bravo.Infrastructure;
    using Sqlbi.Bravo.Infrastructure.Extensions;
    using Sqlbi.Bravo.Infrastructure.Helpers;
    using Sqlbi.Bravo.Models;
    using Sqlbi.Bravo.Models.FormatDax;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Threading;
    using System.Threading.Tasks;
    using TOM = Microsoft.AnalysisServices.Tabular;

    public interface IPBIDesktopService
    {
        IEnumerable<PBIDesktopReport> QueryReports(CancellationToken cancellationToken);

        IEnumerable<PBIDesktopReport> GetReports(CancellationToken cancellationToken);

        Stream ExportVpax(PBIDesktopReport report, bool includeTomModel, bool includeVpaModel, bool readStatisticsFromData, int sampleRows);

        string Update(PBIDesktopReport report, IEnumerable<FormattedMeasure> measures);
    }

    internal class PBIDesktopService : IPBIDesktopService
    {
        public IEnumerable<PBIDesktopReport> QueryReports(CancellationToken cancellationToken)
        {
            var reports = new ConcurrentBag<PBIDesktopReport>();
            var processes = ProcessHelper.GetProcessesByName(AppEnvironment.PBIDesktopProcessName);

            foreach (var process in processes)
            {
                var report = new PBIDesktopReport
                {
                    ProcessId = process.Id,
                    ReportName = process.GetPBIDesktopMainWindowTitle(),

                    // We leave the connection properties empty because we are only interested in the process state and to return the results as fast as possible 
                    ServerName = null,
                    DatabaseName = null,
                    ConnectionMode = PBIDesktopReportConnectionMode.Unknown,
                };

                reports.Add(report);
                process.Dispose();
            }

            return reports;
        }

        public IEnumerable<PBIDesktopReport> GetReports(CancellationToken cancellationToken)
        {
            var reports = new ConcurrentBag<PBIDesktopReport>();

            var processes = ProcessHelper.GetProcessesByName(AppEnvironment.PBIDesktopProcessName);
            var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };
            var parallelLoop = Parallel.ForEach(processes, parallelOptions, (process) =>
            {
                var report = CreateFrom(process);
                reports.Add(report);
                process.Dispose();
            });

            return parallelLoop.IsCompleted ? reports : Array.Empty<PBIDesktopReport>();

            static PBIDesktopReport CreateFrom(Process process)
            {
                var report = new PBIDesktopReport
                {
                    ProcessId = process.Id,
                    ReportName = process.GetPBIDesktopMainWindowTitle(),
                    ServerName = null,
                    DatabaseName = null,
                };

                if (report.ReportName is null)
                {
                    report.ConnectionMode = PBIDesktopReportConnectionMode.UnsupportedProcessNotYetReady;
                }
                else
                {
                    GetConnectionDetails(out var serverName, out var databaseName, out var connectivityMode);
                    report.ServerName = serverName;
                    report.DatabaseName = databaseName;
                    report.ConnectionMode = connectivityMode;
                }

                return report;

                void GetConnectionDetails(out string? serverName, out string? databaseName, out PBIDesktopReportConnectionMode connectivityMode)
                {
                    serverName = null;
                    databaseName = null;

                    var ssasPIDs = process.GetChildrenPIDs(childProcessImageName: AppEnvironment.PBIDesktopSSASProcessImageName).ToArray();
                    if (ssasPIDs.Length != 1)
                    {
                        connectivityMode = PBIDesktopReportConnectionMode.UnsupportedAnalysisServecesProcessNotFound;
                        return;
                    }

                    var ssasPID = ssasPIDs.Single();

                    var ssasConnection = NetworkHelper.GetTcpConnections((c) => c.ProcessId == ssasPID && c.State == TcpState.Listen && IPAddress.IsLoopback(c.EndPoint.Address)).FirstOrDefault();
                    if (ssasConnection == default)
                    {
                        connectivityMode = PBIDesktopReportConnectionMode.UnsupportedAnalysisServecesConnectionNotFound;
                        return;
                    }

                    using var server = new TOM.Server();
                    try
                    {
                        var connectionString = ConnectionStringHelper.BuildForPBIDesktop(ssasConnection.EndPoint);
                        server.Connect(connectionString);
                    }
                    catch (Exception ex)
                    {
                        if (AppEnvironment.IsDiagnosticLevelVerbose)
                            AppEnvironment.AddDiagnostics(DiagnosticMessageType.Text, name: $"{ nameof(PBIDesktopService) }-{ nameof(GetReports) }-{ nameof(GetConnectionDetails) }", ex.ToString(), severity: DiagnosticMessageSeverity.Warning);

                        connectivityMode = PBIDesktopReportConnectionMode.UnsupportedConnectionException;
                        return;
                    }

                    if (server.CompatibilityMode != CompatibilityMode.PowerBI)
                    {
                        connectivityMode = PBIDesktopReportConnectionMode.UnsupportedAnalysisServecesUnexpectedCompatibilityMode;
                        return;
                    }

                    if (server.Databases.Count == 0)
                    {
                        connectivityMode = PBIDesktopReportConnectionMode.UnsupportedDatabaseCollectionIsEmpty;
                        return;
                    }

                    if (server.Databases.Count > 1)
                    {
                        connectivityMode = PBIDesktopReportConnectionMode.UnsupportedDatabaseCollectionUnexpectedCount;
                        return;
                    }

                    var database = server.Databases[0];

                    // Do we need this check ?? (e.g UnsupportedDatabaseNotYetReadyOrUnloaded)
                    // if (database.IsLoaded == false) { }

                    serverName = $"{ NetworkHelper.LocalHost }:{ ssasConnection.EndPoint.Port }"; // we're using 'localhost:<port>' instead of '<ipaddress>:<port>' in order to allow both ipv4 and ipv6 connections 
                    databaseName = database.Name;
                    connectivityMode = PBIDesktopReportConnectionMode.Supported;
                }
            }
        }

        public Stream ExportVpax(PBIDesktopReport report, bool includeTomModel, bool includeVpaModel, bool readStatisticsFromData, int sampleRows)
        {
            var stream = VpaxToolsHelper.ExportVpax(report.ServerName!, report.DatabaseName!, includeTomModel, includeVpaModel, readStatisticsFromData, sampleRows);
            return stream;
        }

        public string Update(PBIDesktopReport report, IEnumerable<FormattedMeasure> measures)
        {
            var databaseETag = TabularModelHelper.Update(report.ServerName!, report.DatabaseName!, measures);
            return databaseETag;
        }
    }
}