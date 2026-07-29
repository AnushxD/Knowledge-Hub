import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from '../state/auth-store';

/**
 * Attaches the session cookie, and reacts to the server dropping it.
 *
 * `withCredentials` is redundant while the dev server proxies `/api` to the
 * API, because that makes every request same-origin. It is here for the
 * deployment where it is not — a client served from a different origin than the
 * API silently sends no cookie at all, and the symptom is a login that appears
 * to work followed by 401s on everything.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthStore);

  return next(request.clone({ withCredentials: true })).pipe(
    catchError((error: unknown) => {
      const status = (error as { status?: number })?.status;

      // Only a session that *was* valid is worth reacting to. The sign-in and
      // identity endpoints answer 401 as a normal outcome, and treating those
      // as an expiry would bounce the user off the login screen they are
      // standing on.
      const isAuthProbe = request.url.includes('/auth/me') || request.url.includes('/auth/login');

      if (status === 401 && !isAuthProbe) {
        auth.onSessionLost();
      }

      return throwError(() => error);
    }),
  );
};
