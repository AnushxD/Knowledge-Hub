import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { LibraryStore } from '../../core/state/library-store';
import { ThemeService } from '../../core/theme/theme.service';
import { Avatar } from '../../shared/components/avatar';
import { Person } from '../../core/models/knowledge.models';
import { AuthStore } from '../../core/state/auth-store';

@Component({
  selector: 'dh-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, FormsModule],
  host: { class: 'block' },
  templateUrl: './settings.html',
})
export class SettingsPage {
  protected readonly theme = inject(ThemeService);
  protected readonly store = inject(LibraryStore);

  protected readonly auth = inject(AuthStore);

  protected readonly currentUser = computed<Person>(() => {
    const user = this.auth.currentUser();

    return {
      id: user?.id ?? 'anonymous',
      name: user?.name ?? 'Signed out',
      initials: user?.initials ?? '?',
      tint: '#7c5cff',
    };
  });

  protected readonly themes = [
    {
      mode: 'dark' as const,
      label: 'Dark',
      hint: 'Default. Easier for long reading sessions.',
      icon: 'pi-moon',
      swatch: '#0f1117',
      fg: '#b09bff',
    },
    {
      mode: 'light' as const,
      label: 'Light',
      hint: 'Higher contrast in bright rooms.',
      icon: 'pi-sun',
      swatch: '#ffffff',
      fg: '#6b45f5',
    },
  ];

  // ---- changing your own password ------------------------------------------

  private readonly gateway = inject(KnowledgeGateway);

  protected readonly changingPassword = signal(false);
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly savingPassword = signal(false);
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordChanged = signal(false);

  /** Matches the server's rule, so the obvious mistakes cost no round trip. */
  private static readonly MinimumPasswordLength = 7;

  /**
   * Why the form cannot be submitted yet, or null when it can.
   *
   * Shown only after a failed attempt rather than as you type: a message
   * telling you the password is too short while you are three characters into
   * typing it is noise.
   */
  protected readonly passwordProblem = computed(() => {
    if (!this.currentPassword()) return 'Enter your current password.';

    if (this.newPassword().length < SettingsPage.MinimumPasswordLength) {
      return `New passwords must be at least ${SettingsPage.MinimumPasswordLength} characters.`;
    }

    if (this.newPassword() !== this.confirmPassword()) return 'The new passwords do not match.';

    if (this.newPassword() === this.currentPassword()) {
      return 'The new password is the same as the current one.';
    }

    return null;
  });

  protected startChangingPassword(): void {
    this.currentPassword.set('');
    this.newPassword.set('');
    this.confirmPassword.set('');
    this.passwordError.set(null);
    this.passwordChanged.set(false);
    this.changingPassword.set(true);
  }

  protected cancelChangingPassword(): void {
    this.changingPassword.set(false);
    this.passwordError.set(null);
  }

  protected changePassword(): void {
    if (this.savingPassword()) return;

    const problem = this.passwordProblem();

    if (problem) {
      this.passwordError.set(problem);
      return;
    }

    this.savingPassword.set(true);
    this.passwordError.set(null);

    this.gateway.changePassword(this.currentPassword(), this.newPassword()).subscribe({
      next: () => {
        this.savingPassword.set(false);
        this.changingPassword.set(false);
        this.passwordChanged.set(true);

        // Not left in the DOM a moment longer than the request needs them.
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmPassword.set('');
      },
      error: (error: unknown) => {
        this.savingPassword.set(false);
        this.passwordError.set(this.describe(error));
      },
    });
  }

  private describe(error: unknown): string {
    const body = error as { error?: { detail?: string; title?: string }; status?: number };

    if (body?.status === 429) {
      return 'Too many attempts. Wait a few minutes and try again.';
    }

    return (
      body?.error?.detail ??
      body?.error?.title ??
      (error instanceof Error && !('status' in error) ? error.message : null) ??
      'The password could not be changed.'
    );
  }

  protected setView(event: Event): void {
    this.store.viewMode.set((event.target as HTMLSelectElement).value as 'list' | 'grid');
  }
}
