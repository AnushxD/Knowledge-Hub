import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TooltipDirective } from '../../shared/directives/tooltip.directive';
import { LibraryStore } from '../../core/state/library-store';
import { AuthStore } from '../../core/state/auth-store';
import { FileKind, IngestionStatus } from '../../core/models/knowledge.models';
import { presentationFor } from '../../core/utils/file-kind';
import { FileIcon } from '../../shared/components/file-icon';
import { StatusPill } from '../../shared/components/status-pill';
import { Avatar } from '../../shared/components/avatar';
import { EmptyState } from '../../shared/components/empty-state';
import { RowSkeleton } from '../../shared/components/row-skeleton';
import { CardSkeleton } from '../../shared/components/card-skeleton';
import { DocumentMenu } from '../../shared/components/document-menu';
import { FileSizePipe, TimeAgoPipe } from '../../shared/pipes/format.pipes';

@Component({
  selector: 'dh-browse',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    TooltipDirective,
    FileIcon,
    StatusPill,
    Avatar,
    EmptyState,
    RowSkeleton,
    CardSkeleton,
    DocumentMenu,
    FileSizePipe,
    TimeAgoPipe,
  ],
  host: {
    class: 'block',
    // An open menu overlays neighbouring cards, so leaving one stuck open is
    // worse here than in a list. The toggle stops propagation, so this only
    // ever fires for a click that landed somewhere else.
    '(document:click)': 'menuFor.set(null)',
    '(document:keydown.escape)': 'menuFor.set(null)',
  },
  templateUrl: './browse.html',
})
export class Browse {
  protected readonly store = inject(LibraryStore);
  protected readonly auth = inject(AuthStore);

  protected readonly documents = computed(() => this.store.documents() ?? []);
  protected readonly filtersOpen = signal(false);
  protected readonly menuFor = signal<string | null>(null);

  protected readonly allStatuses: IngestionStatus[] = ['indexed', 'indexing', 'pending', 'failed'];

  /**
   * What an empty library actually means here.
   *
   * Three different situations look identical on screen — never synced, synced
   * and the repository has no readable files, or a sync that failed — and only
   * the last is a fault. Saying which one it is beats a single reassuring
   * sentence that is wrong two times in three.
   */
  protected readonly emptyMessage = computed(() => {
    const repo = this.store.repository();
    if (!repo) return 'Loading the repository…';

    // A fourth: the hub has not been pointed at a repository at all. Nothing is
    // broken and no sync is missing — there is simply nowhere to mirror from
    // yet, and only an administrator can say where.
    if (!repo.isConfigured) {
      return this.auth.isAdmin()
        ? 'No repository is configured yet. Choose one under Settings, then sync it.'
        : 'No repository is configured yet. An administrator sets which one the hub mirrors.';
    }

    switch (repo.outcome) {
      case 'never':
        return `${repo.projectPath} has not been mirrored yet. Nothing is searchable until it is.`;
      case 'running':
        return `Mirroring ${repo.projectPath} now. Documents appear as they are read.`;
      case 'failed':
        return `The last sync of ${repo.projectPath} failed: ${repo.error ?? 'no reason given'}.`;
      default:
        return repo.skipped > 0
          ? `${repo.projectPath} was read, but none of the files here are of a type that can be indexed — ${repo.skipped} were skipped across the repository.`
          : `${repo.projectPath} was read and had nothing to mirror here.`;
    }
  });

  /**
   * What the last sync actually did, in one line.
   *
   * These numbers were computed, persisted and then shown nowhere, which made
   * a sync indistinguishable from a no-op: pressing "Sync now" against a
   * stalled library requeued six hundred documents and reported the same blank
   * header as a run that changed nothing. Counts that exist and are never
   * rendered are worse than no counts, because the work looks like it did not
   * happen.
   *
   * Null when the failure banner below already says it in full — repeating the
   * same fact twice on one screen reads as two problems.
   */
  protected readonly lastSync = computed(() => {
    const repo = this.store.repository();
    if (!repo) return null;

    // The empty state above already says this in full, and "never synced"
    // beside it would read as a sync that was missed rather than one that has
    // nothing to run against.
    if (!repo.isConfigured) return null;

    switch (repo.outcome) {
      case 'never':
        return 'Never synced.';
      case 'running':
        return 'Reading the repository now…';
      case 'failed':
        return null;
    }

    const parts: string[] = [];
    if (repo.added) parts.push(`${repo.added} added`);
    if (repo.updated) parts.push(`${repo.updated} updated`);
    if (repo.removed) parts.push(`${repo.removed} removed`);
    // Named apart from the rest: nothing in the repository changed, this is
    // the mirror catching up with documents that never finished indexing.
    if (repo.requeued) parts.push(`${repo.requeued} requeued for indexing`);
    if (repo.skipped) parts.push(`${repo.skipped} not indexable`);

    // "No changes" rather than an empty line. A repeat sync of an unchanged
    // repository is the common case and a successful outcome, and saying so is
    // what separates it from a sync that never ran.
    return parts.length > 0 ? parts.join(', ') : 'no changes';
  });

  protected heading(): string {
    if (this.store.starredOnly()) return 'Starred';
    if (this.store.statuses().length === 1 && this.store.statuses()[0] === 'failed')
      return 'Needs attention';
    return this.store.currentFolder()?.name ?? 'All documents';
  }

  protected readonly availableKinds = computed(() => {
    const kinds = new Set<FileKind>();
    for (const doc of this.store.documents() ?? []) kinds.add(doc.kind);
    return [...kinds];
  });

  protected kindLabel(kind: FileKind): string {
    return presentationFor(kind).label;
  }

  protected statusLabel(status: IngestionStatus): string {
    return { indexed: 'Indexed', indexing: 'Indexing', pending: 'Queued', failed: 'Failed' }[
      status
    ];
  }

  protected onSearch(event: Event): void {
    this.store.text.set((event.target as HTMLInputElement).value);
  }

  protected onSort(event: Event): void {
    this.store.sort.set((event.target as HTMLSelectElement).value as never);
  }

  /**
   * Opens this document's menu and closes any other.
   *
   * Stops propagation so the document-level dismiss handler does not treat the
   * opening click as a click outside and close it again immediately.
   */
  protected toggleMenu(event: Event, id: string): void {
    event.stopPropagation();
    this.menuFor.set(this.menuFor() === id ? null : id);
  }

  protected reindex(id: string): void {
    this.menuFor.set(null);
    this.store.retry(id);
  }
}
