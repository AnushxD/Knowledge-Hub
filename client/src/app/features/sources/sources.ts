import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { KnowledgeSource, KnowledgeSourceState } from '../../core/models/knowledge.models';

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
  host: { class: 'block' },
  templateUrl: './sources.html',
})
export class SourcesPage {
  private readonly gateway = inject(KnowledgeGateway);

  protected readonly failed = signal<string | null>(null);

  private readonly sources = toSignal(
    this.gateway.knowledgeSources().pipe(
      map((sources): SourceRow[] | null => sources.map((source) => this.toRow(source))),
      catchError((error: unknown) => {
        this.failed.set(
          error instanceof Error ? error.message : 'Knowledge sources could not be loaded.',
        );
        return of<SourceRow[] | null>([]);
      }),
    ),
    { initialValue: null as SourceRow[] | null },
  );

  protected readonly rows = computed(() => this.sources() ?? []);
  protected readonly loading = computed(() => this.sources() === null && !this.failed());

  /** How many sources actually contribute — the number the answers depend on. */
  protected readonly activeCount = computed(
    () => this.rows().filter((source) => source.state === 'active').length,
  );

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
