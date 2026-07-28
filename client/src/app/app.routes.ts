import { Routes } from '@angular/router';
import { Shell } from './layout/shell';

export const routes: Routes = [
  {
    path: '',
    component: Shell,
    children: [
      {
        path: '',
        title: 'Home · DocHub',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'browse',
        title: 'Library · DocHub',
        loadComponent: () => import('./features/browse/browse').then((m) => m.Browse),
      },
      {
        path: 'docs/:id',
        title: 'Document · DocHub',
        loadComponent: () =>
          import('./features/document-detail/document-detail').then((m) => m.DocumentDetailPage),
      },
      {
        path: 'search',
        title: 'Search · DocHub',
        loadComponent: () => import('./features/search/search').then((m) => m.SearchPage),
      },
      {
        path: 'chat',
        title: 'Assistant · DocHub',
        loadComponent: () =>
          import('./features/roadmap/chat-placeholder').then((m) => m.ChatPlaceholder),
      },
      {
        path: 'sources',
        title: 'Knowledge sources · DocHub',
        loadComponent: () =>
          import('./features/roadmap/sources-placeholder').then((m) => m.SourcesPlaceholder),
      },
      {
        path: 'settings',
        title: 'Settings · DocHub',
        loadComponent: () => import('./features/settings/settings').then((m) => m.SettingsPage),
      },
      { path: '**', redirectTo: '' },
    ],
  },
];
