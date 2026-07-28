import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';

import { routes } from './app.routes';
import { KnowledgeGateway } from './core/data/knowledge-gateway';
import { HttpKnowledgeGateway } from './core/data/http-knowledge-gateway';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),
    provideHttpClient(withFetch()),

    // The single seam between the UI and the backend. `MockKnowledgeGateway`
    // still implements the same contract and can be swapped back in here to
    // develop screens without running the API.
    { provide: KnowledgeGateway, useClass: HttpKnowledgeGateway },
  ],
};
