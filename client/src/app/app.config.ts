import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';

import { routes } from './app.routes';
import { KnowledgeGateway } from './core/data/knowledge-gateway';
import { MockKnowledgeGateway } from './core/data/mock-knowledge-gateway';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),
    provideHttpClient(withFetch()),

    // Phase 1 runs entirely on mock data. Swapping this single line for
    // `HttpKnowledgeGateway` is the whole migration to the real API.
    { provide: KnowledgeGateway, useClass: MockKnowledgeGateway },
  ],
};
