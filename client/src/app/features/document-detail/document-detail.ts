import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
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

type Tab = 'preview' | 'chunks';

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
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
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
    return document ? (RENDERERS[document.kind] ?? 'none') : 'none';
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
          // metadata and chunks are both still worth showing.
          catchError(() =>
            of<TextState>({
              state: 'error',
              message:
                'The stored file could not be read, so only the extracted text is available.',
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
    if (url === null) return null;

    // A cited page is put in the fragment rather than scrolled to: the frame is
    // the browser's own PDF viewer and nothing outside it can reach its pages.
    // `#page=` is the one lever it does offer, and it is the difference between
    // landing on the cited page and landing on page one of a 90-page document.
    const page = this.citedPage();

    return this.sanitizer.bypassSecurityTrustResourceUrl(
      page === null ? url : `${url}#page=${page}`,
    );
  });

  /**
   * The page a citation points at, when the file is a PDF and the section says
   * which. Headings there read "Page 4" — parsed rather than trusted blindly,
   * because a section that names something else is not a page reference.
   */
  private readonly citedPage = computed(() => {
    if (this.renderer() !== 'pdf') return null;

    const section = this.citedSection();
    const page = /^page\s+(\d+)$/i.exec(section?.heading.trim() ?? '');

    return page ? Number(page[1]) : null;
  });

  /** The extracted section a citation points at, if the document has it. */
  private readonly citedSection = computed(() => {
    const chunk = this.highlightChunk();
    if (chunk == null) return null;

    return this.doc()?.sections.find((section) => section.chunkId === chunk) ?? null;
  });

  /**
   * Whether the cited passage can be shown in the rendered document.
   *
   * The rendered document is where a reader would rather land — it is the file
   * as its author wrote it, with its headings, tables and formatting intact,
   * while the extracted view is the same text flattened for the indexer. So the
   * preview is preferred wherever the passage can actually be found in it, and
   * the extracted view is the fallback rather than the destination.
   */
  private readonly citationFitsPreview = computed(() => {
    if (!this.canRender() || this.citedSection() === null) return false;

    switch (this.renderer()) {
      // Both are rendered from text we hold, so the passage can be located in
      // them — unless the text could not be fetched at all.
      case 'markdown':
      case 'code':
        return this.textError() === null;

      // Only when the section names a page; without one there is nowhere to
      // send the viewer and it would open at page one.
      case 'pdf':
        return this.citedPage() !== null;

      // An image has no passages, and a format with no renderer has no preview.
      default:
        return false;
    }
  });

  /** `?chunk=N` — the deep link a citation points at. */
  protected readonly highlightChunk = toSignal(
    this.route.queryParamMap.pipe(map((p) => (p.get('chunk') ? Number(p.get('chunk')) : null))),
    { initialValue: null },
  );

  constructor() {
    // Take the reader to the cited passage once there is something to take
    // them to.
    effect(() => {
      const chunk = this.highlightChunk();
      const loaded = !this.loading();
      if (chunk == null || !loaded) return;

      this.tab.set('preview');

      const inPreview = this.citationFitsPreview();
      this.showExtracted.set(!inPreview);

      // Read so this re-runs when the file's text arrives: the metadata comes
      // back first, and until the body has painted there is no heading or line
      // to scroll to.
      this.textBody();

      // One frame is not enough — the Markdown is parsed and written through
      // [innerHTML], which lands after change detection rather than during it.
      setTimeout(() => this.revealCitation(chunk), 60);
    });
  }

  protected viewDocument(): void {
    this.showExtracted.set(false);

    // The highlight is kept. It used to be cleared here, because only the
    // extracted view could show it; the rendered document can now show it too,
    // and dropping it on the way in would leave a reader who followed a
    // citation looking for their passage by hand.
    const chunk = this.highlightChunk();
    if (chunk != null) setTimeout(() => this.revealCitation(chunk), 60);
  }

  /** The extracted view, keeping the cited passage in view as it opens. */
  protected viewExtracted(): void {
    this.showExtracted.set(true);

    const chunk = this.highlightChunk();
    if (chunk != null) setTimeout(() => this.revealCitation(chunk), 60);
  }

  /**
   * Scrolls to the cited passage in whichever view is open, and marks it.
   *
   * Falls back to the extracted view when the passage cannot be found in the
   * rendered one — a heading the parser rendered differently, a line the
   * extractor rewrote. Landing on the right document with nothing highlighted
   * is the "citations resolve to the passage" guarantee failing quietly, and a
   * plainer view that definitely holds the passage beats a prettier one that
   * may not.
   */
  private revealCitation(chunkId: number): void {
    if (this.showExtracted()) {
      this.scrollTo(this.host.nativeElement.querySelector(`#chunk-${chunkId}`));
      return;
    }

    const found =
      this.renderer() === 'markdown'
        ? this.revealInMarkdown(chunkId)
        : this.renderer() === 'code'
          ? this.revealInCode(chunkId)
          : // A PDF is already open at the right page through the frame's
            // fragment; there is nothing here to scroll.
            this.renderer() === 'pdf';

    if (found) return;

    this.showExtracted.set(true);
    setTimeout(() => this.scrollTo(this.host.nativeElement.querySelector(`#chunk-${chunkId}`)), 60);
  }

  /**
   * Finds the cited section in the rendered Markdown by its heading.
   *
   * Chunks never span sections, so the heading a section was cut at is exactly
   * where its passage starts. Repeated headings — a document with three
   * "Overview"s — are told apart by counting how many earlier sections carry
   * the same one, which works because sections arrive in document order.
   */
  private revealInMarkdown(chunkId: number): boolean {
    const sections = this.doc()?.sections ?? [];
    const index = sections.findIndex((section) => section.chunkId === chunkId);

    if (index < 0) return false;

    const wanted = DocumentDetailPage.normalise(sections[index].heading);
    if (wanted.length === 0) return false;

    const occurrence = sections
      .slice(0, index)
      .filter((section) => DocumentDetailPage.normalise(section.heading) === wanted).length;

    const matches = [
      ...this.host.nativeElement.querySelectorAll<HTMLElement>(
        '.dh-prose h1, .dh-prose h2, .dh-prose h3, .dh-prose h4, .dh-prose h5, .dh-prose h6',
      ),
    ].filter((heading) => DocumentDetailPage.normalise(heading.textContent ?? '') === wanted);

    const target = matches[occurrence] ?? matches[0];
    if (!target) return false;

    this.markPassage(this.sectionElements(target));
    this.scrollTo(target);

    return true;
  }

  /**
   * The heading and everything under it, stopping at the next heading of the
   * same rank or higher — which is where the next section, and so the next
   * chunk, begins.
   */
  private sectionElements(heading: HTMLElement): HTMLElement[] {
    const rank = Number(heading.tagName.slice(1));
    const elements = [heading];

    for (
      let sibling = heading.nextElementSibling;
      sibling instanceof HTMLElement;
      sibling = sibling.nextElementSibling
    ) {
      const siblingRank = /^H[1-6]$/.test(sibling.tagName) ? Number(sibling.tagName.slice(1)) : 0;

      if (siblingRank > 0 && siblingRank <= rank) break;

      elements.push(sibling);
    }

    return elements;
  }

  /**
   * Finds the cited section in the rendered source by where its text sits in
   * the file.
   *
   * A code or plain-text file has no headings to aim at, so the passage is
   * located by its own first line: the extracted text of one of these is the
   * file itself, which makes an exact match the normal case rather than a
   * hopeful one.
   */
  private revealInCode(chunkId: number): boolean {
    const body = this.textBody();
    const section = this.doc()?.sections.find((candidate) => candidate.chunkId === chunkId);

    if (!body || !section) return false;

    const opening = section.body
      .split('\n')
      .map((line) => line.trim())
      .find((line) => line.length > 0);

    const at = opening ? body.replace(/\r\n?/g, '\n').indexOf(opening) : -1;
    if (at < 0) return false;

    const first = body.slice(0, at).split('\n').length - 1;
    const height = section.body.replace(/\r\n?/g, '\n').trimEnd().split('\n').length;

    const lines = [...this.host.nativeElement.querySelectorAll<HTMLElement>('.dh-code-line')].slice(
      first,
      first + height,
    );

    if (lines.length === 0) return false;

    this.markPassage(lines);
    this.scrollTo(lines[0]);

    return true;
  }

  /**
   * Marks the passage, and only the passage. Every previous mark is cleared
   * first — following a second citation into the same document must not leave
   * the first one lit as though both were being quoted.
   */
  private markPassage(elements: HTMLElement[]): void {
    for (const marked of this.host.nativeElement.querySelectorAll('.dh-cited-passage')) {
      marked.classList.remove('dh-cited-passage');
    }

    for (const element of elements) element.classList.add('dh-cited-passage');
  }

  /**
   * Centred rather than aligned to the top: this screen has a sticky header
   * several hundred pixels deep, and "start" would put the passage underneath
   * it.
   *
   * Instant, not smooth. A cited passage in a long file is tens of thousands of
   * pixels down, and the browser abandons a smooth scroll over that distance —
   * which is exactly what this looked like when first built: the passage
   * correctly marked, and the page still sitting at the top. Animating a
   * journey nobody wants to watch was never the point.
   */
  /**
   * Centred rather than aligned to the top: this screen has a sticky header
   * several hundred pixels deep, and "start" would put the passage underneath
   * it.
   *
   * Instant, not smooth. A cited passage in a long file sits tens of thousands
   * of pixels down, and a smooth scroll over that distance simply never
   * arrives — measured on this document: 22,728 pixels away, the page had not
   * moved at all half a minute later. Animating a journey nobody wants to
   * watch was never the point of following a citation.
   */
  private scrollTo(element: Element | null): void {
    element?.scrollIntoView({ behavior: 'auto', block: 'center' });
  }

  /** Headings compared on their words, not their punctuation or spacing. */
  private static normalise(text: string): string {
    return text.replace(/\s+/g, ' ').trim().toLowerCase();
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
