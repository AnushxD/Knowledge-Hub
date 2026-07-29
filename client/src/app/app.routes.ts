import { Routes } from '@angular/router';
import { Shell } from './layout/shell';
import { adminGuard, authGuard } from './core/data/auth-guard';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · Document Hub',
    loadComponent: () => import('./features/auth/login').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: Shell,
    // Everything inside the shell needs a session. The API refuses an
    // unauthenticated caller anyway; this is so a signed-out visitor sees a
    // login form rather than a screen of failed requests.
    canActivate: [authGuard],
    children: [
      {
        path: '',
        title: 'Home · Document Hub',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'browse',
        title: 'Library · Document Hub',
        loadComponent: () => import('./features/browse/browse').then((m) => m.Browse),
      },
      {
        path: 'docs/:id',
        title: 'Document · Document Hub',
        loadComponent: () =>
          import('./features/document-detail/document-detail').then((m) => m.DocumentDetailPage),
      },
      {
        path: 'search',
        title: 'Search · Document Hub',
        loadComponent: () => import('./features/search/search').then((m) => m.SearchPage),
      },
      {
        path: 'chat',
        title: 'Assistant · Document Hub',
        loadComponent: () => import('./features/chat/chat').then((m) => m.ChatPage),
      },
      {
        path: 'sources',
        title: 'Knowledge sources · Document Hub',
        loadComponent: () => import('./features/sources/sources').then((m) => m.SourcesPage),
      },
      {
        path: 'users',
        title: 'People · Document Hub',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/users/users').then((m) => m.UsersPage),
      },
      {
        path: 'settings',
        title: 'Settings · Document Hub',
        loadComponent: () => import('./features/settings/settings').then((m) => m.SettingsPage),
      },
      { path: '**', redirectTo: '' },
    ],
  },
];
