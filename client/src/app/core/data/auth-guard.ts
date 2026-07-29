import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthStore } from '../state/auth-store';

/**
 * Keeps signed-out visitors on the login screen.
 *
 * This is navigation, not security. Every endpoint behind these screens refuses
 * an unauthenticated caller on its own — the guard exists so a signed-out user
 * sees a login form instead of a screen full of failed requests.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  const allow = () =>
    auth.isSignedIn()
      ? true
      : router.createUrlTree(['/login'], {
          queryParams: { returnUrl: state.url },
        });

  // On a cold load nobody has asked the server yet, so the first navigation
  // waits for the answer rather than guessing and redirecting a perfectly
  // valid session to the login screen.
  return auth.isReady() ? allow() : auth.restore().pipe(map(allow));
};

/** Admin-only screens. Same caveat: the API is what actually enforces this. */
export const adminGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  const allow = () => {
    if (!auth.isSignedIn()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    return auth.isAdmin() ? true : router.createUrlTree(['/']);
  };

  return auth.isReady() ? allow() : auth.restore().pipe(map(allow));
};
