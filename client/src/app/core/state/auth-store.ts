import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { KnowledgeGateway } from '../data/knowledge-gateway';
import { SignedInUser, UserRole } from '../models/knowledge.models';

/**
 * Who is signed in, for the whole app.
 *
 * The server is the authority — this is a cache of what it last said, and every
 * decision it drives is a *presentation* decision. Hiding an upload button from
 * a viewer is a courtesy; the endpoint refuses them regardless, which is what
 * actually enforces the rule.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly gateway = inject(KnowledgeGateway);
  private readonly router = inject(Router);

  private readonly user = signal<SignedInUser | null>(null);

  /** False until the first `/auth/me` answers, so nothing renders on a guess. */
  private readonly resolved = signal(false);

  readonly currentUser = this.user.asReadonly();
  readonly isReady = this.resolved.asReadonly();
  readonly isSignedIn = computed(() => this.user() !== null);

  readonly role = computed<UserRole | null>(() => this.user()?.role ?? null);

  /** Whether the signed-in user may change content. Mirrors the server's policy. */
  readonly canContribute = computed(() => {
    const role = this.role();
    return role === 'Admin' || role === 'Editor';
  });

  readonly isAdmin = computed(() => this.role() === 'Admin');

  /**
   * Asks the server who this is. Called once at startup and after signing in.
   */
  restore(): Observable<SignedInUser | null> {
    return this.gateway.currentUser().pipe(
      tap((user) => {
        this.user.set(user);
        this.resolved.set(true);
      }),
    );
  }

  signIn(email: string, password: string): Observable<SignedInUser> {
    return this.gateway.signIn(email, password).pipe(
      tap((user) => {
        this.user.set(user);
        this.resolved.set(true);
      }),
    );
  }

  signOut(): void {
    // The local state is cleared whether or not the server call succeeds. A
    // failed sign-out that left the user looking signed in is the one outcome
    // worse than an extra round trip.
    this.gateway.signOut().subscribe({
      next: () => this.clearAndRedirect(),
      error: () => this.clearAndRedirect(),
    });
  }

  /**
   * Called by the interceptor when any request comes back 401 — the session
   * expired or was revoked somewhere else.
   */
  onSessionLost(): void {
    if (!this.isSignedIn()) return;

    this.clearAndRedirect();
  }

  private clearAndRedirect(): void {
    this.user.set(null);
    this.resolved.set(true);

    // Remembers where they were, so signing back in returns them to it rather
    // than to the dashboard.
    void this.router.navigate(['/login'], {
      queryParams: { returnUrl: this.router.url },
    });
  }
}
