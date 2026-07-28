import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap } from 'rxjs';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { LibraryStore } from '../../core/state/library-store';
import { FileKind, MatchStrategy, SearchResponse } from '../../core/models/knowledge.models';
import { presentationFor } from '../../core/utils/file-kind';
import { FileIcon } from '../../shared/components/file-icon';
import { EmptyState } from '../../shared/components/empty-state';
import { RowSkeleton } from '../../shared/components/row-skeleton';
import { TooltipDirective } from '../../shared/directives/tooltip.directive';

/** A snippet split into plain and matched runs, so the template never renders HTML. */
interface SnippetPart {
  text: string;
  matched: boolean;
}

@Component({
  selector: 'dh-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, FileIcon, EmptyState, RowSkeleton, TooltipDirective],
  host: { class: 'block' },
  templateUrl: './search.html',
})
export class SearchPage {
  private readonly gateway = inject(KnowledgeGateway);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly store = inject(LibraryStore);

  /** What the user is typing. Only committed to the URL on submit. */
  protected readonly draft = signal('');

  protected readonly kinds = signal<FileKind[]>([]);
  protected readonly folderScope = signal<string | null>(null);

  /**
   * The URL is the source of truth for the executed query, so a search can be
   * linked to, reloaded and navigated back to — the same contract the library
   * screen already honours.
   */
  private readonly query = toSignal(this.route.queryParamMap.pipe(map((p) => p.get('q') ?? '')), {
    initialValue: '',
  });

  protected readonly searching = signal(false);
  protected readonly failed = signal<string | null>(null);

  private readonly response = toSignal(
    this.route.queryParamMap.pipe(
      map((params) => params.get('q')?.trim() ?? ''),
      switchMap((text) => {
        if (!text) return of(null);

        this.searching.set(true);
        this.failed.set(null);

        return this.gateway
          .search({ text, folderId: this.folderScope(), kinds: this.kinds() })
          .pipe(
            map((result) => {
              this.searching.set(false);
              return result;
            }),
            catchError((error: unknown) => {
              this.searching.set(false);
              this.failed.set(
                error instanceof Error ? error.message : 'The search request failed.',
              );
              return of(null);
            }),
          );
      }),
    ),
    { initialValue: null as SearchResponse | null },
  );

  protected readonly results = computed(() => this.response()?.results ?? []);
  protected readonly diagnostics = computed(() => this.response()?.diagnostics ?? null);
  protected readonly total = computed(() => this.response()?.totalMatches ?? 0);
  protected readonly elapsed = computed(() => this.response()?.elapsedMs ?? 0);
  protected readonly hasQuery = computed(() => this.query().trim().length > 0);

  constructor() {
    // Keep the input in step with the URL, including on back/forward.
    effect(() => this.draft.set(this.query()));
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value);
  }

  protected submit(): void {
    const text = this.draft().trim();

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: text || null },
      queryParamsHandling: 'merge',
    });
  }

  protected clear(): void {
    this.draft.set('');
    void this.router.navigate([], { relativeTo: this.route, queryParams: { q: null } });
  }

  protected toggleKind(kind: FileKind): void {
    this.kinds.update((current) =>
      current.includes(kind) ? current.filter((k) => k !== kind) : [...current, kind],
    );
    this.rerun();
  }

  protected setScope(folderId: string | null): void {
    this.folderScope.set(folderId);
    this.rerun();
  }

  /**
   * Filters live in component state rather than the URL, so changing one has
   * to nudge the query param stream to re-fire.
   */
  private rerun(): void {
    if (!this.hasQuery()) return;

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: this.query(), t: Date.now() },
    });
  }

  protected kindLabel(kind: FileKind): string {
    return presentationFor(kind).label;
  }

  protected readonly availableKinds = computed<FileKind[]>(() => {
    const kinds = new Set<FileKind>();
    for (const document of this.store.documents() ?? []) kinds.add(document.kind);
    return [...kinds];
  });

  /**
   * Splits a snippet on the searched terms so matches can be marked up with
   * real elements. The server sends terms as data and never as markup, so this
   * is the only place highlighting exists — nothing untrusted is ever bound as
   * HTML.
   */
  protected highlight(snippet: string): SnippetPart[] {
    const terms = (this.response()?.terms ?? []).filter((term) => term.length > 1);
    if (!terms.length) return [{ text: snippet, matched: false }];

    // Longest first, so "database" wins over "data" where they overlap.
    const pattern = [...terms]
      .sort((a, b) => b.length - a.length)
      .map(escapeRegExp)
      .join('|');

    const parts: SnippetPart[] = [];

    // Word boundaries matter more than they look: without them "in" marks the
    // middle of "printing" and the snippet turns to confetti.
    const regex = new RegExp(`\\b(${pattern})\\b`, 'gi');
    let lastIndex = 0;

    for (const match of snippet.matchAll(regex)) {
      const index = match.index ?? 0;
      if (index > lastIndex) {
        parts.push({ text: snippet.slice(lastIndex, index), matched: false });
      }
      parts.push({ text: match[0], matched: true });
      lastIndex = index + match[0].length;
    }

    if (lastIndex < snippet.length) {
      parts.push({ text: snippet.slice(lastIndex), matched: false });
    }

    return parts;
  }

  protected strategyLabel(strategy: MatchStrategy): string {
    return { keyword: 'Keyword', vector: 'Semantic', both: 'Keyword + semantic' }[strategy];
  }

  protected strategyTooltip(strategy: MatchStrategy): string {
    return {
      keyword: 'Found by full-text search — this passage contains your words.',
      vector: 'Found by embedding similarity — this passage is about your question, ' +
        'even without matching words.',
      both: 'Found by both branches, which is why it ranks highest.',
    }[strategy];
  }

  protected readonly suggestions = [
    'how do I run this locally',
    'connection string',
    'what happens when ingestion fails',
  ];

  protected runSuggestion(text: string): void {
    this.draft.set(text);
    this.submit();
  }
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
