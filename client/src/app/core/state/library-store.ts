import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { startWith, switchMap } from 'rxjs';
import { KnowledgeGateway } from '../data/knowledge-gateway';
import {
  DocumentQuery,
  DocumentSummary,
  FileKind,
  Folder,
  IngestionStatus,
  SortKey,
} from '../models/knowledge.models';

export type ViewMode = 'list' | 'grid';

/**
 * Screen-level state for the document browser. Components read signals from
 * here and never talk to the gateway directly, which keeps filtering logic in
 * one testable place.
 */
@Injectable({ providedIn: 'root' })
export class LibraryStore {
  private readonly gateway = inject(KnowledgeGateway);

  // ---- filter state -------------------------------------------------------
  readonly folderId = signal<string | null>(null);
  readonly text = signal('');
  readonly kinds = signal<FileKind[]>([]);
  readonly statuses = signal<IngestionStatus[]>([]);
  readonly tags = signal<string[]>([]);
  readonly ownerId = signal<string | undefined>(undefined);
  readonly starredOnly = signal(false);
  readonly sort = signal<SortKey>('updated-desc');
  readonly viewMode = signal<ViewMode>('list');

  readonly query = computed<DocumentQuery>(() => ({
    folderId: this.folderId(),
    recursive: true,
    text: this.text(),
    kinds: this.kinds(),
    statuses: this.statuses(),
    tags: this.tags(),
    ownerId: this.ownerId(),
    starredOnly: this.starredOnly(),
    sort: this.sort(),
  }));

  readonly activeFilterCount = computed(
    () =>
      this.kinds().length +
      this.statuses().length +
      this.tags().length +
      (this.ownerId() ? 1 : 0) +
      (this.starredOnly() ? 1 : 0) +
      (this.text() ? 1 : 0),
  );

  // ---- data ---------------------------------------------------------------
  private readonly refresh = signal(0);

  /**
   * Bumped after every mutation made through this store.
   *
   * Exposed so a screen that fetches its own data — the document detail page
   * reads one document rather than the list — can re-read after a change it
   * made here. Without it such a screen sends the change and then keeps
   * rendering the state from before, which looks exactly like the action
   * having done nothing.
   */
  readonly revision = this.refresh.asReadonly();

  readonly folders = toSignal<Folder[] | undefined>(
    toObservable(this.refresh).pipe(switchMap(() => this.gateway.folders())),
    { initialValue: undefined },
  );

  private readonly refresh$ = toObservable(this.refresh);

  /**
   * Re-runs when the filters change *or* after a mutation. The refresh trigger
   * matters for the HTTP gateway: unlike the in-memory mock it answers a query
   * once, so an upload or delete would otherwise leave the list stale.
   *
   * The `undefined` that drives the loading skeleton is emitted **once per
   * query**, not once per refresh. A new filter genuinely has nothing to show
   * yet; a refresh of the same query already has rows on screen, and blanking
   * them would flash the skeleton over a list that is about to look almost
   * identical — which is unbearable while polling an ingesting document.
   */
  private readonly documentsResult = toSignal<DocumentSummary[] | undefined>(
    toObservable(this.query).pipe(
      switchMap((query) =>
        this.refresh$.pipe(
          switchMap(() => this.gateway.documents(query)),
          startWith(undefined as DocumentSummary[] | undefined),
        ),
      ),
    ),
    { initialValue: undefined },
  );

  readonly documents = computed(() => this.documentsResult());
  readonly loading = computed(() => this.documentsResult() === undefined);

  readonly stats = toSignal(
    toObservable(this.refresh).pipe(switchMap(() => this.gateway.stats())),
    { initialValue: undefined },
  );

  readonly activity = toSignal(
    toObservable(this.refresh).pipe(switchMap(() => this.gateway.activity(9))),
    { initialValue: undefined },
  );

  /**
   * True while any document is still moving through ingestion.
   *
   * Only these two states change on their own. Everything else changes because
   * somebody did something, and is already covered by the refresh after that
   * action.
   */
  private readonly ingesting = computed(() =>
    (this.documentsResult() ?? []).some(
      (document) => document.status === 'pending' || document.status === 'indexing',
    ),
  );

  /**
   * Whether anyone is actually looking. A background tab should not keep
   * polling — nobody sees the result, and an app left open overnight on a
   * document that is stuck would re-read forever.
   */
  private readonly visible = signal(!document.hidden);

  constructor() {
    document.addEventListener('visibilitychange', () => this.visible.set(!document.hidden));

    // Re-read while anything is ingesting, so an uploaded document moves
    // Queued → Indexing → Indexed on screen instead of only on reload.
    //
    // The whole refresh, not just the list: finishing ingestion also changes
    // the chunk count, the folder counts, the dashboard totals and the activity
    // trail. Refreshing only the rows would leave those disagreeing with it.
    //
    // A re-armed timeout rather than an interval: the effect re-runs when the
    // refreshed list arrives, so the next read is scheduled from the response
    // rather than from a fixed clock, and two requests can never overlap on a
    // slow connection.
    effect((onCleanup) => {
      if (!this.ingesting() || !this.visible()) return;

      const handle = setTimeout(() => this.bump(), PollIntervalMs);
      onCleanup(() => clearTimeout(handle));
    });
  }

  readonly people = toSignal(this.gateway.people(), { initialValue: [] });
  readonly availableTags = toSignal(this.gateway.allTags(), { initialValue: [] });

  readonly currentFolder = computed(() => {
    const id = this.folderId();
    return id ? this.folders()?.find((f) => f.id === id) : undefined;
  });

  readonly breadcrumb = computed<Folder[]>(() => {
    const folders = this.folders() ?? [];
    const trail: Folder[] = [];
    let cursor = this.currentFolder();
    while (cursor) {
      trail.unshift(cursor);
      cursor = folders.find((f) => f.id === cursor!.parentId);
    }
    return trail;
  });

  readonly rootFolders = computed(() => (this.folders() ?? []).filter((f) => f.parentId === null));

  childrenOf(parentId: string | null): Folder[] {
    return (this.folders() ?? []).filter((f) => f.parentId === parentId);
  }

  // ---- commands -----------------------------------------------------------
  openFolder(id: string | null): void {
    this.folderId.set(id);
  }

  clearFilters(): void {
    this.text.set('');
    this.kinds.set([]);
    this.statuses.set([]);
    this.tags.set([]);
    this.ownerId.set(undefined);
    this.starredOnly.set(false);
  }

  toggleKind(kind: FileKind): void {
    this.kinds.update((k) => (k.includes(kind) ? k.filter((x) => x !== kind) : [...k, kind]));
  }

  toggleStatus(status: IngestionStatus): void {
    this.statuses.update((s) =>
      s.includes(status) ? s.filter((x) => x !== status) : [...s, status],
    );
  }

  toggleTag(tag: string): void {
    this.tags.update((t) => (t.includes(tag) ? t.filter((x) => x !== tag) : [...t, tag]));
  }

  showOnlyFailed(): void {
    this.clearFilters();
    this.folderId.set(null);
    this.statuses.set(['failed']);
  }

  showStarred(): void {
    this.clearFilters();
    this.folderId.set(null);
    this.starredOnly.set(true);
  }

  star(documentId: string): void {
    this.gateway.toggleStar(documentId).subscribe(() => this.bump());
  }

  retry(documentId: string): void {
    this.gateway.retryIngestion(documentId).subscribe(() => this.bump());
  }

  remove(documentId: string): void {
    this.gateway.deleteDocument(documentId).subscribe(() => this.bump());
  }

  move(documentId: string, folderId: string): void {
    this.gateway.moveDocument(documentId, folderId).subscribe(() => this.bump());
  }

  upload(folderId: string, files: File[]): void {
    this.gateway.uploadFiles(folderId, files).subscribe(() => this.bump());
  }

  createFolder(parentId: string | null, name: string): void {
    this.gateway.createFolder(parentId, name).subscribe(() => this.bump());
  }

  /**
   * Deletes a folder, everything under it, and every file those documents own.
   *
   * Moves the view out of the subtree *before* the request, not after: if the
   * folder currently being browsed is inside what is about to disappear, the
   * list would otherwise re-query a folder the server no longer has and answer
   * with nothing — which reads as "your documents are gone" rather than "you
   * deleted this folder".
   */
  deleteFolder(id: string): void {
    if (this.isInSubtree(this.folderId(), id)) this.openFolder(null);

    this.gateway.deleteFolder(id).subscribe(() => this.bump());
  }

  /** Whether `folderId` is `ancestorId` or sits somewhere beneath it. */
  private isInSubtree(folderId: string | null, ancestorId: string): boolean {
    const folders = this.folders() ?? [];
    let cursor = folderId;

    while (cursor) {
      if (cursor === ancestorId) return true;
      cursor = folders.find((folder) => folder.id === cursor)?.parentId ?? null;
    }

    return false;
  }

  /**
   * Invalidate the derived reads (folders, stats, activity) after a mutation.
   * The document list refreshes on its own because the gateway streams it.
   */
  private bump(): void {
    this.refresh.update((n) => n + 1);
  }
}

/**
 * How often to re-read while something is ingesting.
 *
 * Ingestion of a normal document takes seconds, so this is the difference
 * between watching it finish and wondering whether it has. Polling rather than
 * a pushed event because the work runs in-process on Hangfire with no
 * notification channel out of it, and a second long-lived stream through IIS is
 * a lot of moving parts for a status that is only interesting for a few seconds.
 */
const PollIntervalMs = 2_500;
