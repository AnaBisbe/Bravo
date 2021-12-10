﻿using Microsoft.Identity.Client;
using Sqlbi.Bravo.Infrastructure.Helpers;
using System.Linq;
using System.Threading.Tasks;

namespace Sqlbi.Bravo.Services
{
    internal class AuthenticationService : IAuthenticationService
    {
        private const string MicrosoftAccountOnlyQueryParameter = "msafed=0";

        // AuthorityUri     "https://login.microsoftonline.com/common"
        // ClientId         "ea0616ba-638b-4df5-95b9-636659ae5121"
        // EndpointUri  "https://api.powerbi.com"
        // Name         "Public"
        // RedirectUri      "https://login.microsoftonline.com/common/oauth2/nativeclient"
        // ResourceUri  "https://analysis.windows.net/powerbi/api"
        // Scopes       { "https://analysis.windows.net/powerbi/api/.default" }

        private static readonly PublicClientApplicationOptions _options = new()
        {
            Instance = "https://login.microsoftonline.com/",
            TenantId = "common",
            ClientId = "ea0616ba-638b-4df5-95b9-636659ae5121",
            RedirectUri = "https://login.microsoftonline.com/common/oauth2/nativeclient"
        };

        private static readonly string[] _scopes =
        {
            "https://analysis.windows.net/powerbi/api/.default"
        };

        // TODO: load from IConfiguration.Get<PublicClientApplicationOptions>()
        //private static readonly PublicClientApplicationOptions _options = new()
        //{
        //    Instance = "https://login.microsoftonline.com/",
        //    TenantId = "f545bd66-7c3f-4729-851a-b7ca3ac9fb6e", // "common"
        //    ClientId = "4257bc9e-ed10-490f-a73e-5c9ba120bf8f",
        //};
        //private static readonly string[] _scopes =
        //{
        //    //"api://2daa3311-aaad-4eed-bb63-da2466f0f88f/access_as_user"
        //};

        private static readonly IPublicClientApplication _application;

        static AuthenticationService()
        {
            _application = PublicClientApplicationBuilder.Create(_options.ClientId)
                .WithAuthority($"{ _options.Instance }{ _options.TenantId }")
                //.WithRedirectUri(_options.RedirectUri)
                .WithDefaultRedirectUri()
                .Build();

            // TODO: enable token cache
            // TokenCacheHelper.EnableSerialization(_application.UserTokenCache);
        }

        public async Task<AuthenticationResult> AcquireTokenAsync(string identifier)
        {
            // Use account used to signed-in in Windows (WAM). WAM will always get an account in the cache.
            // So if we want to have a chance to select the accounts interactively, we need to force the non-account
            //account = PublicClientApplication.OperatingSystemAccount;

            // Use one of the Accounts known by Windows (WAM). We force WAM to display the dialog with the accounts
            //account = null;

            var account = await _application.GetAccountAsync(identifier).ConfigureAwait(false);
            
            try
            {
                // Try to acquire an access token from the cache, if UI interaction is required, MsalUiRequiredException will be thrown.
                var authenticationResult = await _application.AcquireTokenSilent(_scopes, account).ExecuteAsync().ConfigureAwait(false);
                return authenticationResult;
            }
            catch (MsalUiRequiredException /* murex */)
            {
                try
                {
                    // Force a sign-in (Prompt.SelectAccount), as the MSAL web browser might contain cookies
                    // for the current user and we don't necessarily want to re-sign-in the same user
                    var builder = _application.AcquireTokenInteractive(_scopes)
                        //.WithAccount(account)
                        //.WithClaims(murex.Claims)
                        //.WithParentActivityOrWindow(new WindowInteropHelper(Program.HostWindow).Handle) // optional, used to center the browser on the window
                        //.WithParentActivityOrWindow(Program.HostWindow!.WindowHandle) // optional, used to center the browser on the window
                        //.WithPrompt(Prompt.SelectAccount);
                        .WithExtraQueryParameters(MicrosoftAccountOnlyQueryParameter);

                    //if (!_application.IsEmbeddedWebViewAvailable())
                    //{
                        // You app should install the embedded browser WebView2 https://aka.ms/msal-net-webview2
                        // but if for some reason this is not possible, you can fall back to the system browser 
                        // in this case, the redirect uri needs to be set to "http://localhost"
                        //builder = builder.WithUseEmbeddedWebView(useEmbeddedWebView: false);
                    //}

                    //builder = builder.WithSystemWebViewOptions(GetCustomWebViewOptions()); // Using the custom html
                    //builder = builder.WithCustomWebUi(customWebUi: new LoginWebUI(Program.HostWindow.WindowHandle)); //Using our custom web ui

                    var authenticationResult = await builder.ExecuteAsync().ConfigureAwait(false);
                    return authenticationResult;
                }
                catch (MsalException mex) 
                {
                    if (mex.ErrorCode == "access_denied") // ex.ErrorCode => Microsoft.Identity.Client.MsalError
                    {
                        // The user canceled sign in, take no action.
                    }

                    throw;
                }
            }
        }

        public async Task ClearTokenCache()
        {
            var accounts = (await _application.GetAccountsAsync().ConfigureAwait(false)).ToArray();

            // Clears the library cache, does not affect the browser cookies
            while (accounts.Length > 0)
            {
                await _application.RemoveAsync(accounts[0]).ConfigureAwait(false);
                accounts = (await _application.GetAccountsAsync()).ToArray();
            }
        }

        //private SystemWebViewOptions GetCustomWebViewOptions()
        //{
        //    return new SystemWebViewOptions
        //    {
        //        HtmlMessageSuccess = @"<html style='font-family: sans-serif;'>
        //                          <head><title>Authentication Complete</title></head>
        //                          <body style='text-align: center;'>
        //                              <header>
        //                                  <h1>Custom Web UI</h1>
        //                              </header>
        //                              <main style='border: 1px solid lightgrey; margin: auto; width: 600px; padding-bottom: 15px;'>
        //                                  <h2 style='color: limegreen;'>Authentication complete</h2>
        //                                  <div>You can return to the application. Feel free to close this browser tab.</div>
        //                              </main>

        //                          </body>
        //                        </html>",

        //        HtmlMessageError = @"<html style='font-family: sans-serif;'>
        //                      <head><title>Authentication Failed</title></head>
        //                      <body style='text-align: center;'>
        //                          <header>
        //                              <h1>Custom Web UI</h1>
        //                          </header>
        //                          <main style='border: 1px solid lightgrey; margin: auto; width: 600px; padding-bottom: 15px;'>
        //                              <h2 style='color: salmon;'>Authentication failed</h2>
        //                              <div><b>Error details:</b> error {0} error_description: {1}</div>
        //                              <br>
        //                              <div>You can return to the application. Feel free to close this browser tab.</div>
        //                          </main>

        //                      </body>
        //                    </html>"
        //    };
        //}
    }
}
