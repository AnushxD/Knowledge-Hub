import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';

import { routes } from './app.routes';
import { KnowledgeGateway } from './core/data/knowledge-gateway';
import { HttpKnowledgeGateway } from './core/data/http-knowledge-gateway';
import { authInterceptor } from './core/data/auth-interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),
    // The interceptor carries the session cookie and reacts to the server
    // dropping it, so no screen has to handle a 401 of its own.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),

    // The single seam between the UI and the backend. `MockKnowledgeGateway`
    // still implements the same contract and can be swapped back in here to
    // develop screens without running the API.
    { provide: KnowledgeGateway, useClass: HttpKnowledgeGateway },
  ],
};
