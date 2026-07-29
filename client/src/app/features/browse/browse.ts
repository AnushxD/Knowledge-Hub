import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
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
import { FileSizePipe, TimeAgoPipe } from '../../shared/pipes/format.pipes';
import { UploadDialog } from './upload-dialog';

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
    UploadDialog,
    FileSizePipe,
    TimeAgoPipe,
  ],
  host: { class: 'block' },
  templateUrl: './browse.html',
})
export class Browse {
  protected readonly store = inject(LibraryStore);
  protected readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly documents = computed(() => this.store.documents() ?? []);
  protected readonly filtersOpen = signal(false);
  protected readonly showUpload = signal(false);
  protected readonly menuFor = signal<string | null>(null);
  protected readonly pageDragging = signal(false);
  protected readonly droppedFiles = signal<File[]>([]);

  protected readonly allStatuses: IngestionStatus[] = ['indexed', 'indexing', 'pending', 'failed'];

  /** `?upload=1` opens the dialog — used by the dashboard and command palette. */
  private readonly uploadParam = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('upload'))),
    { initialValue: null },
  );

  constructor() {
    // Open the dialog when routed to with ?upload=1.
    queueMicrotask(() => {
      if (this.uploadParam()) this.showUpload.set(true);
    });
  }

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

  protected toggleOwner(id: string): void {
    this.store.ownerId.set(this.store.ownerId() === id ? undefined : id);
  }

  protected newFolder(): void {
    const name = prompt('Folder name');
    if (name?.trim()) this.store.createFolder(this.store.folderId(), name.trim());
  }

  protected star(event: Event, id: string): void {
    event.preventDefault();
    event.stopPropagation();
    this.store.star(id);
  }

  protected reindex(id: string): void {
    this.menuFor.set(null);
    this.store.retry(id);
  }

  protected del(id: string): void {
    this.menuFor.set(null);
    this.store.remove(id);
  }

  // ---- page-level drop target --------------------------------------------
  protected onDragOver(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    this.pageDragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    if (event.currentTarget === event.target) this.pageDragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (!files.length) return;
    event.preventDefault();
    this.pageDragging.set(false);
    this.droppedFiles.set(files);
    this.showUpload.set(true);
  }

  protected closeUpload(): void {
    this.showUpload.set(false);
    this.droppedFiles.set([]);
    if (this.uploadParam()) this.router.navigate([], { queryParams: {} });
  }
}
