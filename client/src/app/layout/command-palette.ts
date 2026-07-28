import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { KnowledgeGateway } from '../core/data/knowledge-gateway';
import { LibraryStore } from '../core/state/library-store';
import { ThemeService } from '../core/theme/theme.service';
import { CommandPaletteService } from './command-palette.service';
import { FileIcon } from '../shared/components/file-icon';
import { StatusPill } from '../shared/components/status-pill';

interface PaletteItem {
  id: string;
  kind: 'action' | 'document' | 'folder' | 'ask';
  label: string;
  hint?: string;
  icon?: string;
  run: () => void;
  raw?: unknown;
}

@Component({
  selector: 'dh-command-palette',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FileIcon, StatusPill],
  host: { class: 'contents' },
  templateUrl: './command-palette.html',
  styleUrl: './command-palette.css',
})
export class CommandPalette {
  protected readonly palette = inject(CommandPaletteService);
  private readonly gateway = inject(KnowledgeGateway);
  private readonly store = inject(LibraryStore);
  private readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  private readonly input = viewChild<ElementRef<HTMLInputElement>>('input');

  protected readonly term = signal('');
  protected readonly index = signal(0);

  /** Unscoped document list — the palette searches the whole library. */
  private readonly allDocuments = toSignal(this.gateway.documents({ sort: 'updated-desc' }), {
    initialValue: [],
  });

  constructor() {
    effect(() => {
      if (this.palette.open()) {
        this.term.set('');
        this.index.set(0);
        queueMicrotask(() => this.input()?.nativeElement.focus());
      }
    });
  }

  private readonly actions = computed<PaletteItem[]>(() => {
    const items: PaletteItem[] = [
      {
        id: 'act-upload',
        kind: 'action',
        icon: 'pi-upload',
        label: 'Upload documents',
        hint: 'Add files to the library',
        run: () => this.go(['/browse'], { upload: 1 }),
      },
      {
        id: 'act-library',
        kind: 'action',
        icon: 'pi-folder',
        label: 'Go to library',
        run: () => this.go(['/browse']),
      },
      {
        id: 'act-failed',
        kind: 'action',
        icon: 'pi-exclamation-triangle',
        label: 'Show documents that failed to index',
        run: () => {
          this.store.showOnlyFailed();
          this.go(['/browse']);
        },
      },
      {
        id: 'act-theme',
        kind: 'action',
        icon: 'pi-moon',
        label: 'Toggle light / dark theme',
        run: () => {
          this.theme.toggle();
          this.palette.hide();
        },
      },
    ];
    const term = this.term().toLowerCase();
    return term ? items.filter((i) => i.label.toLowerCase().includes(term)) : items;
  });

  private readonly documentMatches = computed<PaletteItem[]>(() => {
    const term = this.term().trim().toLowerCase();
    const docs = this.allDocuments() ?? [];
    const matched = term
      ? docs.filter((d) =>
          `${d.title} ${d.fileName} ${d.tags.join(' ')}`.toLowerCase().includes(term),
        )
      : docs.slice(0, 5);
    return matched.slice(0, 7).map((doc) => ({
      id: doc.id,
      kind: 'document' as const,
      label: doc.title,
      hint: `${doc.fileName} · v${doc.version}`,
      raw: doc,
      run: () => this.go(['/docs', doc.id]),
    }));
  });

  private readonly folderMatches = computed<PaletteItem[]>(() => {
    const term = this.term().trim().toLowerCase();
    if (!term) return [];
    return (this.store.folders() ?? [])
      .filter((f) => f.path.toLowerCase().includes(term))
      .slice(0, 5)
      .map((folder) => ({
        id: folder.id,
        kind: 'folder' as const,
        icon: 'pi-folder',
        label: folder.name,
        hint: folder.path,
        run: () => {
          this.store.clearFilters();
          this.store.openFolder(folder.id);
          this.go(['/browse']);
        },
      }));
  });

  /**
   * Hands the query to full-text search. The palette only matches titles and
   * tags; this is the way through to the content itself.
   */
  private readonly searchItem = computed<PaletteItem[]>(() => {
    const term = this.term().trim();
    if (term.length < 2) return [];
    return [
      {
        id: 'search',
        kind: 'action',
        icon: 'pi-search',
        label: `Search inside documents for “${term}”`,
        hint: 'Keyword and semantic matching over every indexed passage',
        run: () => this.go(['/search'], { q: term }),
      },
    ];
  });

  /** Phase 3 hook: the same box will hand the query to the assistant. */
  private readonly askItem = computed<PaletteItem[]>(() => {
    const term = this.term().trim();
    if (term.length < 4) return [];
    return [
      {
        id: 'ask',
        kind: 'ask',
        icon: 'pi-sparkles',
        label: `Ask the assistant: “${term}”`,
        hint: 'Grounded answers with citations — phase 3',
        run: () => this.go(['/chat'], { q: term }),
      },
    ];
  });

  protected readonly groups = computed(() => [
    { title: 'Search', items: this.searchItem() },
    { title: 'Ask', items: this.askItem() },
    { title: this.term() ? 'Documents' : 'Recent documents', items: this.documentMatches() },
    { title: 'Folders', items: this.folderMatches() },
    { title: 'Actions', items: this.actions() },
  ]);

  private readonly flat = computed(() => this.groups().flatMap((g) => g.items));
  protected readonly total = computed(() => this.flat().length);
  protected readonly hasResults = computed(() => this.total() > 0);

  protected flatIndex(item: PaletteItem): number {
    return this.flat().findIndex((i) => i.id === item.id);
  }

  protected setIndex(i: number): void {
    this.index.set(i);
  }

  protected asDoc(item: PaletteItem) {
    return item.raw as import('../core/models/knowledge.models').DocumentSummary;
  }

  protected onInput(event: Event): void {
    this.term.set((event.target as HTMLInputElement).value);
    this.index.set(0);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const items = this.flat();
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.index.update((i) => (i + 1) % Math.max(1, items.length));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.index.update((i) => (i - 1 + items.length) % Math.max(1, items.length));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      items[this.index()]?.run();
    } else if (event.key === 'Escape') {
      this.palette.hide();
    }
  }

  private go(commands: unknown[], queryParams?: Record<string, unknown>): void {
    this.palette.hide();
    this.router.navigate(commands, queryParams ? { queryParams } : undefined);
  }
}
