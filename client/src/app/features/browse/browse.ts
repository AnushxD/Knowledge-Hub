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
