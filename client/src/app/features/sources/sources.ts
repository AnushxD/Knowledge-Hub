import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { AuthStore } from '../../core/state/auth-store';
import {
  KnowledgeSource,
  KnowledgeSourceState,
  RepositoryProbe,
  RepositorySource,
} from '../../core/models/knowledge.models';

/** How one state is drawn: its glyph, its label, and the accent it carries. */
interface StatePresentation {
  icon: string;
  label: string;
  /** A `--dh-status-*` token, so this screen shares the app's one status palette. */
  color: string;
}

/** A source with everything the template needs already resolved. */
interface SourceRow extends KnowledgeSource {
  icon: string;
  presentation: StatePresentation;
}

/**
 * What the assistant is allowed to ground an answer in, and whether each source
 * is contributing right now.
 *
 * The screen's job is honesty rather than reassurance. A source that is off by
 * design says so and explains what would turn it on; a source that should be
 * working and is not is drawn differently, because a permanently red light is
 * one people learn to ignore.
 */
@Component({
  selector: 'dh-sources',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  host: { class: 'block' },
  templateUrl: './sources.html',
})
export class SourcesPage {
  private readonly gateway = inject(KnowledgeGateway);
  protected readonly auth = inject(AuthStore);

  protected readonly failed = signal<string | null>(null);

  // A writable signal rather than toSignal: saving a new address changes what
  // the status lines say, so this list has to be re-read rather than only
  // fetched once on load.
  private readonly sourceRows = signal<SourceRow[] | null>(null);

  protected readonly rows = computed(() => this.sourceRows() ?? []);
  protected readonly loading = computed(() => this.sourceRows() === null && !this.failed());

  /** How many sources actually contribute — the number the answers depend on. */
  protected readonly activeCount = computed(
    () => this.rows().filter((source) => source.state === 'active').length,
  );

  // ---- repository source administration (admins only) ----------------------

  protected readonly repository = signal<RepositorySource | null>(null);
  protected readonly editing = signal(false);
  protected readonly draftEndpoint = signal('');
  protected readonly draftEnabled = signal(true);
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  protected readonly probe = signal<RepositoryProbe | null>(null);
  protected readonly editFailure = signal<string | null>(null);

  constructor() {
    this.reloadSources();

    // Only an admin can read this endpoint, so only an admin asks. A viewer
    // requesting it would get a 403 the interceptor would rightly ignore, but
    // the console noise would be ours.
    if (this.auth.isAdmin()) this.loadRepository();
  }

  protected startEditing(): void {
    const current = this.repository();

    this.draftEndpoint.set(current?.endpoint ?? '');

    // Editing an existing address reflects whatever it actually is; setting one
    // for the first time defaults to on, because nobody types in a server
    // address in order to leave it switched off.
    this.draftEnabled.set(current?.endpoint ? current.isEnabled : true);
    this.probe.set(null);
    this.editFailure.set(null);
    this.editing.set(true);
  }

  protected save(): void {
    if (this.saving()) return;

    this.saving.set(true);
    this.editFailure.set(null);

    const endpoint = this.draftEndpoint().trim() || null;

    this.gateway.saveRepositorySource(endpoint, this.draftEnabled()).subscribe({
      next: (saved) => {
        this.repository.set(saved);
        this.saving.set(false);
        this.editing.set(false);
        // The status line on the card above is now stale — it is computed from
        // this same setting on the server.
        this.reloadSources();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.editFailure.set(this.describe(error, 'The address could not be saved.'));
      },
    });
  }

  protected reset(): void {
    this.saving.set(true);
    this.editFailure.set(null);

    this.gateway.resetRepositorySource().subscribe({
      next: (saved) => {
        this.repository.set(saved);
        this.saving.set(false);
        this.editing.set(false);
        this.reloadSources();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.editFailure.set(this.describe(error, 'The override could not be cleared.'));
      },
    });
  }

  protected test(): void {
    if (this.testing()) return;

    this.testing.set(true);
    this.probe.set(null);
    this.editFailure.set(null);

    this.gateway.testRepositorySource(this.draftEndpoint().trim() || null).subscribe({
      next: (result) => {
        this.probe.set(result);
        this.testing.set(false);
      },
      error: (error: unknown) => {
        this.testing.set(false);
        this.editFailure.set(this.describe(error, 'The address could not be tested.'));
      },
    });
  }

  protected cancel(): void {
    this.editing.set(false);
    this.probe.set(null);
    this.editFailure.set(null);
  }

  private loadRepository(): void {
    this.gateway.repositorySource().subscribe({
      next: (source) => this.repository.set(source),
      // Silent: this panel is an extra for admins, and failing to load it must
      // not take the sources list down with it.
      error: () => this.repository.set(null),
    });
  }

  private reloadSources(): void {
    this.gateway.knowledgeSources().subscribe({
      next: (sources) => {
        this.sourceRows.set(sources.map((source) => this.toRow(source)));
        this.failed.set(null);
      },
      error: (error: unknown) => {
        this.sourceRows.set([]);
        this.failed.set(this.describe(error, 'Knowledge sources could not be loaded.'));
      },
    });
  }

  private describe(error: unknown, fallback: string): string {
    const detail = (error as { error?: { detail?: string; title?: string } })?.error;
    return detail?.detail ?? detail?.title ?? fallback;
  }

  private toRow(source: KnowledgeSource): SourceRow {
    return {
      ...source,
      icon: SourcesPage.ICONS[source.name] ?? 'pi-sitemap',
      presentation: SourcesPage.STATES[source.state],
    };
  }

  /**
   * Keyed on the source's stable name, with a generic glyph for anything this
   * build has not seen. A source added on the server must not be able to break
   * this screen.
   */
  private static readonly ICONS: Record<string, string> = {
    documents: 'pi-database',
    repositories: 'pi-github',
  };

  /**
   * Reuses the ingestion palette rather than introducing a second one: a user
   * has already learned that green contributes, grey is idle and rose needs
   * attention, and a status vocabulary is only worth learning once.
   */
  private static readonly STATES: Record<KnowledgeSourceState, StatePresentation> = {
    active: {
      icon: 'pi-check-circle',
      label: 'Active',
      color: 'var(--dh-status-indexed)',
    },
    inactive: {
      icon: 'pi-ban',
      label: 'Not configured',
      color: 'var(--dh-status-pending)',
    },
    unavailable: {
      icon: 'pi-exclamation-triangle',
      label: 'Unavailable',
      color: 'var(--dh-status-failed)',
    },
  };
}
