/*!
 * Bravo for Power BI
 * Copyright (c) SQLBI corp. - All rights reserved.
 * https://www.sqlbi.com
*/
import { Doc } from '../model/doc';
import { i18n } from '../model/i18n';
import { strings } from '../model/strings';
import { MainScene } from './scene-main';

export class BestPracticesScene extends MainScene {
    
    constructor(id: string, container: HTMLElement, doc: Doc) {
        super(id, container, doc);
        this.title = `${i18n(strings.BestPractices)} » ${doc.name}`;
        
        this.element.classList.add("best-practices");
    }

    render() {
        super.render();
        
        let html = `

        `;
        this.body.insertAdjacentHTML("beforeend", html);
    }
}