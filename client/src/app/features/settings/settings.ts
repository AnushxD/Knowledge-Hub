import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { LibraryStore } from '../../core/state/library-store';
import { ThemeService } from '../../core/theme/theme.service';
import { Avatar } from '../../shared/components/avatar';
import {
  Person,
  RepositoryConnection,
  RepositorySettings,
  RepositorySettingsDraft,
} from '../../core/models/knowledge.models';
import { AuthStore } from '../../core/state/auth-store';

@Component({
  selector: 'dh-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, DatePipe, FormsModule],
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

  constructor() {
    // Only an admin can read this, so only an admin asks: a viewer's request
    // would be refused, and the console noise would be ours.
    if (this.auth.isAdmin()) this.loadRepository();
  }

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

  private describe(error: unknown, fallback = 'The password could not be changed.'): string {
    const body = error as { error?: { detail?: string; title?: string }; status?: number };

    if (body?.status === 429) {
      return 'Too many attempts. Wait a few minutes and try again.';
    }

    return (
      body?.error?.detail ??
      body?.error?.title ??
      (error instanceof Error && !('status' in error) ? error.message : null) ??
      fallback
    );
  }

  // ---- the mirrored repository (admins only) -------------------------------
  //
  // Which repository the library comes from is a deployment-level setting, and
  // pointing the hub somewhere else replaces every document in it at the next
  // sync. The API enforces the role; this section is hidden from everyone else
  // because a control that only ever returns 403 is worse than no control.

  protected readonly repository = signal<RepositorySettings | null>(null);
  protected readonly editingRepository = signal(false);
  protected readonly savingRepository = signal(false);
  protected readonly testingRepository = signal(false);
  protected readonly repositoryError = signal<string | null>(null);
  protected readonly repositorySaved = signal(false);
  protected readonly connection = signal<RepositoryConnection | null>(null);

  protected readonly baseUrl = signal('');
  protected readonly projectPath = signal('');
  protected readonly branch = signal('');
  protected readonly subPath = signal('');

  /** Blank leaves the stored secret alone — the screen never had it to send back. */
  protected readonly newToken = signal('');
  protected readonly newWebhookSecret = signal('');
  protected readonly clearToken = signal(false);
  protected readonly clearWebhookSecret = signal(false);

  /** Why the form cannot be saved yet, or null when it can. */
  protected readonly repositoryProblem = computed(() => {
    if (!/^https?:\/\/\S+$/.test(this.baseUrl().trim())) {
      return 'The instance address must be a full http or https URL, such as https://gitlab.example.org.';
    }

    const project = this.projectPath().trim();

    if (!project) return 'Give the project path, as GitLab spells it — team/docs.';

    // The mistake that looks right and fails as a bare 404 from GitLab.
    if (project.includes('://')) {
      return 'The project path is the part after the instance address — team/docs, not the whole URL.';
    }

    if (!this.branch().trim()) return 'Name the branch to mirror, such as main.';

    return null;
  });

  /**
   * Whether saving would change which files the library holds.
   *
   * Worth saying out loud before the fact: the next sync removes every document
   * that is not in the new tree, which for a different project is all of them.
   */
  protected readonly libraryWillBeReplaced = computed(() => {
    const saved = this.repository();

    if (!saved?.isConfigured) return false;

    return (
      this.projectPath()
        .trim()
        .replace(/^\/|\/$/g, '') !== saved.projectPath ||
      this.branch().trim() !== saved.branch ||
      this.subPath()
        .trim()
        .replace(/^\/|\/$/g, '') !== saved.subPath
    );
  });

  /** Whether the last test found a working repository, for how the panel is drawn. */
  protected readonly connectionWorked = computed(() => {
    const result = this.connection();

    return (
      result !== null &&
      result.isReachable &&
      result.projectFound &&
      result.branchFound &&
      result.subPathFound
    );
  });

  protected loadRepository(): void {
    this.gateway.repositorySettings().subscribe({
      next: (settings) => this.repository.set(settings),
      error: (error: unknown) =>
        this.repositoryError.set(
          this.describe(error, 'The repository settings could not be read.'),
        ),
    });
  }

  protected startEditingRepository(): void {
    const settings = this.repository();

    this.baseUrl.set(settings?.baseUrl ?? '');
    this.projectPath.set(settings?.projectPath ?? '');

    // A branch is the one field with a sensible default: main is what a fresh
    // project gets, and an empty box invites saving without one.
    this.branch.set(settings?.branch || 'main');
    this.subPath.set(settings?.subPath ?? '');

    this.clearRepositoryFormState();
    this.editingRepository.set(true);
  }

  protected cancelEditingRepository(): void {
    this.editingRepository.set(false);
    this.clearRepositoryFormState();
  }

  protected testRepository(): void {
    if (this.testingRepository()) return;

    const problem = this.repositoryProblem();

    if (problem) {
      this.repositoryError.set(problem);
      return;
    }

    this.testingRepository.set(true);
    this.repositoryError.set(null);
    this.connection.set(null);

    this.gateway.testRepositorySettings(this.repositoryDraft()).subscribe({
      next: (result) => {
        this.testingRepository.set(false);
        this.connection.set(result);
      },
      error: (error: unknown) => {
        this.testingRepository.set(false);
        this.repositoryError.set(this.describe(error, 'The repository could not be tested.'));
      },
    });
  }

  protected saveRepository(): void {
    if (this.savingRepository()) return;

    const problem = this.repositoryProblem();

    if (problem) {
      this.repositoryError.set(problem);
      return;
    }

    this.savingRepository.set(true);
    this.repositoryError.set(null);

    this.gateway.saveRepositorySettings(this.repositoryDraft()).subscribe({
      next: (settings) => {
        this.savingRepository.set(false);
        this.repository.set(settings);
        this.editingRepository.set(false);
        this.repositorySaved.set(true);
        this.newToken.set('');
        this.newWebhookSecret.set('');
        this.clearToken.set(false);
        this.clearWebhookSecret.set(false);
      },
      error: (error: unknown) => {
        this.savingRepository.set(false);
        this.repositoryError.set(this.describe(error, 'The repository could not be saved.'));
      },
    });
  }

  /**
   * The change to send. A secret is left out entirely unless it is being
   * replaced or cleared, which is what keeps an unchanged token off the wire.
   */
  private repositoryDraft(): RepositorySettingsDraft {
    const draft: RepositorySettingsDraft = {
      baseUrl: this.baseUrl().trim(),
      projectPath: this.projectPath().trim(),
      branch: this.branch().trim(),
      subPath: this.subPath().trim(),
    };

    if (this.clearToken()) draft.token = '';
    else if (this.newToken()) draft.token = this.newToken();

    if (this.clearWebhookSecret()) draft.webhookSecret = '';
    else if (this.newWebhookSecret()) draft.webhookSecret = this.newWebhookSecret();

    return draft;
  }

  private clearRepositoryFormState(): void {
    this.repositoryError.set(null);
    this.repositorySaved.set(false);
    this.connection.set(null);
    this.newToken.set('');
    this.newWebhookSecret.set('');
    this.clearToken.set(false);
    this.clearWebhookSecret.set(false);
  }

  protected setView(event: Event): void {
    this.store.viewMode.set((event.target as HTMLSelectElement).value as 'list' | 'grid');
  }
}
