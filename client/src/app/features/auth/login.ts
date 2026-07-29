import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { AuthStore } from '../../core/state/auth-store';
import { AuthOptions } from '../../core/models/knowledge.models';

/**
 * The sign-in screen.
 *
 * Deliberately outside the shell: there is no navigation, no folder tree and no
 * assistant to offer someone who is not signed in, and rendering the chrome
 * around an empty app only invites clicks that 401.
 */
@Component({
  selector: 'dh-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  host: { class: 'block' },
  templateUrl: './login.html',
})
export class LoginPage {
  private readonly auth = inject(AuthStore);
  private readonly gateway = inject(KnowledgeGateway);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly options = toSignal(
    this.gateway.authOptions().pipe(catchError(() => of({ googleEnabled: false }))),
    { initialValue: { googleEnabled: false } as AuthOptions },
  );

  constructor() {
    // A redirect back from Google carries the reason it refused. Translated
    // here rather than shown raw, because "domain" means nothing to the person
    // who just tried to sign in with their personal account.
    const error = this.route.snapshot.queryParamMap.get('error');
    if (error) this.failure.set(LoginPage.MESSAGES[error] ?? LoginPage.MESSAGES['external']);
  }

  protected submit(): void {
    if (this.submitting()) return;

    const email = this.email().trim();
    const password = this.password();

    if (!email || !password) {
      this.failure.set('Enter your email and password.');
      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    this.auth.signIn(email, password).subscribe({
      next: () => {
        this.submitting.set(false);
        void this.router.navigateByUrl(this.returnUrl());
      },
      error: (error: unknown) => {
        this.submitting.set(false);

        // The server deliberately does not say whether the account exists, so
        // neither does this. Its own message is shown when it has one — a
        // lockout is worth explaining, since typing more carefully will not fix
        // it.
        const detail = (error as { error?: { detail?: string } })?.error?.detail;
        this.failure.set(detail ?? 'That email and password do not match an account.');
      },
    });
  }

  protected signInWithGoogle(): void {
    // A full page navigation, not an XHR: the browser has to follow redirects
    // to Google and back for the flow to work at all.
    window.location.href = `/api/auth/google/start?returnUrl=${encodeURIComponent(this.returnUrl())}`;
  }

  private returnUrl(): string {
    const requested = this.route.snapshot.queryParamMap.get('returnUrl');

    // Only same-app paths. A returnUrl is attacker-supplied by definition, and
    // following one off-site would turn signing in into a redirect to anywhere.
    return requested?.startsWith('/') && !requested.startsWith('//') ? requested : '/';
  }

  private static readonly MESSAGES: Record<string, string> = {
    domain: 'That Google account is not on an allowed domain. Sign in with your company account.',
    unverified:
      'Google could not confirm that address belongs to you. Verify it with Google and try again.',
    noaccount: 'No account here yet. Ask an administrator to create one for you.',
    provision: 'Your account could not be created. Ask an administrator for help.',
    external: 'Google sign-in did not complete. Try again.',
  };
}
