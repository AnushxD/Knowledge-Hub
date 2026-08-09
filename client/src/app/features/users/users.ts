import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { AuthStore } from '../../core/state/auth-store';
import { Account, UserRole } from '../../core/models/knowledge.models';

/**
 * Account administration.
 *
 * Exists because there is no self-registration: without this screen the only
 * accounts an installation can ever have are the seeded administrator and
 * whoever Google auto-provisions.
 */
@Component({
  selector: 'dh-users',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  host: { class: 'block' },
  templateUrl: './users.html',
})
export class UsersPage {
  private readonly gateway = inject(KnowledgeGateway);
  protected readonly auth = inject(AuthStore);

  protected readonly accounts = signal<Account[]>([]);
  protected readonly loading = signal(true);
  protected readonly failure = signal<string | null>(null);

  protected readonly roles: UserRole[] = ['Admin', 'Editor', 'Viewer'];

  protected readonly showForm = signal(false);
  protected readonly draftName = signal('');
  protected readonly draftEmail = signal('');
  protected readonly draftRole = signal<UserRole>('Viewer');
  protected readonly draftPassword = signal('');
  protected readonly saving = signal(false);

  constructor() {
    this.load();
  }

  protected roleHint(role: UserRole): string {
    switch (role) {
      case 'Admin':
        return 'Everything, including this screen and the jobs dashboard.';
      case 'Editor':
        return 'Edit document titles, descriptions and tags, and re-index them.';
      default:
        return 'Read, search and ask the assistant. Cannot change content.';
    }
  }

  protected create(): void {
    if (this.saving()) return;

    const name = this.draftName().trim();
    const email = this.draftEmail().trim();

    if (!name || !email) {
      this.failure.set('A name and an email address are required.');
      return;
    }

    this.saving.set(true);
    this.failure.set(null);

    this.gateway
      .createAccount({
        name,
        email,
        role: this.draftRole(),
        // Blank means Google-only, which the API treats as a real case rather
        // than a mistake.
        password: this.draftPassword().trim() || undefined,
      })
      .subscribe({
        next: (account) => {
          this.accounts.update((current) =>
            [...current, account].sort((a, b) => a.name.localeCompare(b.name)),
          );
          this.saving.set(false);
          this.reset();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.failure.set(this.describe(error, 'The account could not be created.'));
        },
      });
  }

  protected changeRole(account: Account, role: string): void {
    if (role === account.role) return;

    this.gateway.changeAccountRole(account.id, role as UserRole).subscribe({
      next: (updated) => this.replace(updated),
      error: (error: unknown) => {
        this.failure.set(this.describe(error, 'The role could not be changed.'));
        // Put the list back, so the select does not keep showing a change the
        // server refused.
        this.accounts.update((current) => [...current]);
      },
    });
  }

  protected toggleEnabled(account: Account): void {
    this.gateway.setAccountEnabled(account.id, account.isLockedOut).subscribe({
      next: (updated) => this.replace(updated),
      error: (error: unknown) =>
        this.failure.set(this.describe(error, 'The account could not be updated.')),
    });
  }

  protected reset(): void {
    this.showForm.set(false);
    this.draftName.set('');
    this.draftEmail.set('');
    this.draftRole.set('Viewer');
    this.draftPassword.set('');
  }

  private load(): void {
    this.gateway.accounts().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.failure.set(this.describe(error, 'People could not be loaded.'));
      },
    });
  }

  private replace(updated: Account): void {
    this.accounts.update((current) =>
      current.map((account) => (account.id === updated.id ? updated : account)),
    );
  }

  /** Prefers the server's own problem-details message — it names the rule. */
  private describe(error: unknown, fallback: string): string {
    const detail = (error as { error?: { detail?: string; title?: string } })?.error;
    return detail?.detail ?? detail?.title ?? fallback;
  }
}
