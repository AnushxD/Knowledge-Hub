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
  template: `
    @if (palette.open()) {
      <div
        class="fixed inset-0 z-50 flex items-start justify-center px-4 pt-[12vh]"
        role="dialog"
        aria-modal="true"
        aria-label="Command palette"
      >
        <div
          class="absolute inset-0 bg-black/45 backdrop-blur-[2px]"
          (click)="palette.hide()"
          aria-hidden="true"
        ></div>

        <div
          class="dh-rise relative w-full max-w-2xl overflow-hidden rounded-dh-xl border border-hairline bg-surface-1 shadow-dh-lg"
        >
          <div class="flex items-center gap-3 border-b border-hairline px-4">
            <i class="pi pi-search text-[14px] text-subtle"></i>
            <input
              #input
              type="text"
              class="h-12 flex-1 bg-transparent text-[14px] text-ink outline-none placeholder:text-subtle"
              placeholder="Search documents, jump to a folder, or run a command…"
              [value]="term()"
              (input)="onInput($event)"
              (keydown)="onKeydown($event)"
            />
            <kbd
              class="rounded-md border border-hairline px-1.5 py-0.5 font-sans text-[10.5px] text-subtle"
              >esc</kbd
            >
          </div>

          <div class="max-h-[52vh] overflow-y-auto py-2">
            @for (group of groups(); track group.title) {
              @if (group.items.length) {
                <p class="dh-eyebrow px-4 pt-2 pb-1">{{ group.title }}</p>
                @for (item of group.items; track item.id) {
                  <button
                    type="button"
                    class="flex w-full items-center gap-3 px-3 py-2 text-left"
                    [class]="index() === flatIndex(item) ? 'bg-surface-2' : ''"
                    (mouseenter)="setIndex(flatIndex(item))"
                    (click)="item.run()"
                  >
                    @if (item.kind === 'document') {
                      <dh-file-icon [kind]="asDoc(item).kind" size="sm" />
                    } @else {
                      <span
                        class="grid size-7 shrink-0 place-items-center rounded-[9px] border border-hairline bg-surface-2 text-[12px] text-muted"
                      >
                        <i class="pi" [class]="item.icon"></i>
                      </span>
                    }
                    <span class="min-w-0 flex-1">
                      <span class="block truncate text-[13px] text-ink">{{ item.label }}</span>
                      @if (item.hint) {
                        <span class="block truncate text-[11.5px] text-subtle">{{
                          item.hint
                        }}</span>
                      }
                    </span>
                    @if (item.kind === 'document') {
                      <dh-status-pill
                        [status]="asDoc(item).status"
                        [progress]="asDoc(item).indexProgress"
                      />
                    }
                  </button>
                }
              }
            }

            @if (!hasResults()) {
              <div class="px-4 py-10 text-center">
                <p class="text-[13px] text-muted">No matches for “{{ term() }}”.</p>
                <p class="mt-1 text-[12px] text-subtle">
                  Semantic search arrives in phase 2 — right now this matches titles and tags only.
                </p>
              </div>
            }
          </div>

          <div
            class="flex items-center gap-4 border-t border-hairline bg-surface-inset px-4 py-2 text-[11px] text-subtle"
          >
            <span><kbd class="dh-key">↑</kbd><kbd class="dh-key">↓</kbd> navigate</span>
            <span><kbd class="dh-key">↵</kbd> open</span>
            <span class="ml-auto">{{ total() }} results</span>
          </div>
        </div>
      </div>
    }
  `,
  styles: `
    .dh-key {
      display: inline-grid;
      place-items: center;
      min-width: 18px;
      height: 18px;
      margin-right: 3px;
      border: 1px solid var(--dh-border);
      border-radius: 5px;
      background: var(--dh-surface-1);
      font-family: inherit;
      font-size: 10px;
    }
  `,
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
