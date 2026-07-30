import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, map, of, startWith, switchMap } from 'rxjs';
import { TooltipDirective } from '../../shared/directives/tooltip.directive';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { LibraryStore } from '../../core/state/library-store';
import { FileKind } from '../../core/models/knowledge.models';
import { presentationFor } from '../../core/utils/file-kind';
import { FileIcon } from '../../shared/components/file-icon';
import { StatusPill } from '../../shared/components/status-pill';
import { Avatar } from '../../shared/components/avatar';
import { EmptyState } from '../../shared/components/empty-state';
import { MarkdownView } from '../../shared/components/markdown-view';
import { CodeView } from '../../shared/components/code-view';
import { FileSizePipe, TimeAgoPipe } from '../../shared/pipes/format.pipes';

type Tab = 'preview' | 'versions' | 'chunks';

/**
 * How the original file is best shown.
 *
 * `none` is a real answer, not a gap: a Word or PowerPoint file cannot be
 * rendered faithfully in a browser without a converter, so the honest move is
 * the extracted text plus a download, rather than a broken approximation.
 */
type Renderer = 'markdown' | 'code' | 'pdf' | 'image' | 'none';

const RENDERERS: Record<FileKind, Renderer> = {
  markdown: 'markdown',
  text: 'code',
  code: 'code',
  sql: 'code',
  pdf: 'pdf',
  image: 'image',
  word: 'none',
  slides: 'none',
  sheet: 'none',
  diagram: 'none',
  archive: 'none',
  unknown: 'none',
};

/** What the rendered pane is currently doing. */
type TextState =
  | { state: 'idle' }
  | { state: 'loading' }
  | { state: 'ready'; body: string }
  | { state: 'error'; message: string };

@Component({
  selector: 'dh-document-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    TooltipDirective,
    FileIcon,
    StatusPill,
    Avatar,
    EmptyState,
    MarkdownView,
    CodeView,
    FileSizePipe,
    TimeAgoPipe,
  ],
  host: { class: 'block' },
  templateUrl: './document-detail.html',
  styleUrl: './document-detail.css',
})
export class DocumentDetailPage {
  private readonly gateway = inject(KnowledgeGateway);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);
  protected readonly store = inject(LibraryStore);

  /** Bound from the route via `withComponentInputBinding()`. */
  readonly id = input<string>('');

  /**
   * Opens the assistant with this document named in the question.
   *
   * Seeds the box rather than sending it: "ask about this document" is not
   * itself a question, and firing off a half-formed one would spend a slow
   * generation on something nobody asked.
   */
  protected askAbout(document: { title: string }): void {
    void this.router.navigate(['/chat'], {
      queryParams: { draft: `About "${document.title}": ` },
    });
  }

  protected readonly tab = signal<Tab>('preview');
  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'preview', label: 'Preview' },
    { id: 'versions', label: 'Versions' },
    { id: 'chunks', label: 'Chunks' },
  ];

  /**
   * Re-reads on navigation *and* after any mutation through the store, so
   * starring from this screen shows the new state instead of silently
   * succeeding server-side.
   *
   * No `startWith(undefined)` on the refetch: keeping the previous document
   * on screen while the new one arrives avoids flashing the skeleton over an
   * already-loaded page for what is usually a single changed flag.
   */
  private readonly result = toSignal(
    combineLatest([
      this.route.paramMap.pipe(map((params) => params.get('id') ?? '')),
      toObservable(this.store.revision),
    ]).pipe(switchMap(([id]) => this.gateway.document(id))),
    { initialValue: undefined },
  );

  protected readonly doc = computed(() => this.result());
  protected readonly loading = computed(() => this.result() === undefined);

  // ---- preview ------------------------------------------------------------

  /** Which renderer this file type gets, if any. */
  protected readonly renderer = computed<Renderer>(() => {
    const document = this.doc();
    return document ? RENDERERS[document.kind] ?? 'none' : 'none';
  });

  /** A frame or image can only be pointed at a gateway that has a real file. */
  protected readonly contentUrl = computed(() => {
    const document = this.doc();
    return document ? this.gateway.documentContentUrl(document.id) : null;
  });

  /** Null when the gateway has no stored file to hand over. */
  protected readonly downloadUrl = computed(() => {
    const document = this.doc();
    return document ? this.gateway.documentDownloadUrl(document.id) : null;
  });

  /**
   * The document view is the default where we can render the file, because it
   * is what the reader came to read. The extracted view stays one click away —
   * and becomes the default for a citation, below.
   */
  protected readonly showExtracted = signal(false);

  /** Whether this file can be shown as itself at all. */
  protected readonly canRender = computed(() => {
    const renderer = this.renderer();
    if (renderer === 'none') return false;
    // PDFs and images need the file itself; text kinds are fetched instead.
    return renderer === 'markdown' || renderer === 'code' || this.contentUrl() !== null;
  });

  /** Text is fetched only for the renderers that read it. */
  private readonly textSource = computed(() => {
    const document = this.doc();
    if (!document) return null;
    const renderer = this.renderer();
    return renderer === 'markdown' || renderer === 'code' ? document.id : null;
  });

  protected readonly text = toSignal(
    toObservable(this.textSource).pipe(
      switchMap((id) => {
        if (id === null) return of<TextState>({ state: 'idle' });

        return this.gateway.documentText(id).pipe(
          map((body) => ({ state: 'ready', body }) as TextState),
          startWith({ state: 'loading' } as TextState),
          // A preview that fails must not take the whole page down — the
          // metadata, chunks and versions are all still worth showing.
          catchError(() =>
            of<TextState>({
              state: 'error',
              message: 'The stored file could not be read, so only the extracted text is available.',
            }),
          ),
        );
      }),
    ),
    { initialValue: { state: 'idle' } as TextState },
  );

  // Narrowed here rather than in the template: a discriminated union is not
  // reliably narrowed inside @switch, and a cast in the markup would move type
  // safety out of the compiler's reach.
  protected readonly textBody = computed(() => {
    const text = this.text();
    return text.state === 'ready' ? text.body : null;
  });

  protected readonly textError = computed(() => {
    const text = this.text();
    return text.state === 'error' ? text.message : null;
  });

  protected readonly textLoading = computed(() => this.text().state === 'loading');

  /**
   * The PDF frame's URL, marked trusted.
   *
   * An `<iframe src>` is a RESOURCE_URL context, which Angular will not accept
   * from a plain string. Bypassing is sound *because* the value is not user
   * input: it is our own API path plus the document id, same-origin, and the
   * server serves it inline only for a short allow-list of inert types, under a
   * sandbox CSP. The document's own bytes never reach this string.
   */
  protected readonly pdfUrl = computed(() => {
    const url = this.contentUrl();
    return url === null ? null : this.sanitizer.bypassSecurityTrustResourceUrl(url);
  });

  /** `?chunk=N` — the deep link a citation points at. */
  protected readonly highlightChunk = toSignal(
    this.route.queryParamMap.pipe(map((p) => (p.get('chunk') ? Number(p.get('chunk')) : null))),
    { initialValue: null },
  );

  constructor() {
    // Scroll the cited passage into view once the document has rendered.
    effect(() => {
      const chunk = this.highlightChunk();
      const loaded = !this.loading();
      if (chunk == null || !loaded) return;
      this.tab.set('preview');
      // A citation points at a chunk, and only the extracted view has chunks to
      // point at. Arriving from an answer therefore switches to it — otherwise
      // the link would land on a nicely rendered document with nothing
      // highlighted, which is exactly the "citations resolve to the passage"
      // guarantee quietly failing.
      this.showExtracted.set(true);
      setTimeout(
        () =>
          document
            .getElementById(`chunk-${chunk}`)
            ?.scrollIntoView({ behavior: 'smooth', block: 'center' }),
        60,
      );
    });
  }

  protected viewDocument(): void {
    // Dropping the highlight too: the cited passage marker belongs to the
    // extracted view, and leaving it set would send the reader straight back.
    if (this.highlightChunk() != null) this.clearHighlight();
    this.showExtracted.set(false);
  }

  protected kindLabel(kind: FileKind): string {
    return presentationFor(kind).label;
  }

  protected jumpToChunk(chunkId: number): void {
    this.tab.set('preview');
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { chunk: chunkId },
      queryParamsHandling: 'merge',
    });
  }

  protected clearHighlight(): void {
    this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  protected openFolder(folderId: string): void {
    this.store.clearFilters();
    this.store.openFolder(folderId);
    this.router.navigate(['/browse']);
  }

  protected filterByTag(tag: string): void {
    this.store.clearFilters();
    this.store.openFolder(null);
    this.store.toggleTag(tag);
    this.router.navigate(['/browse']);
  }
}
