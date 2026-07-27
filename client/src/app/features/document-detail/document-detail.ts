import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map, switchMap } from 'rxjs';
import { TooltipDirective } from '../../shared/directives/tooltip.directive';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { LibraryStore } from '../../core/state/library-store';
import { FileKind } from '../../core/models/knowledge.models';
import { presentationFor } from '../../core/utils/file-kind';
import { FileIcon } from '../../shared/components/file-icon';
import { StatusPill } from '../../shared/components/status-pill';
import { Avatar } from '../../shared/components/avatar';
import { EmptyState } from '../../shared/components/empty-state';
import { FileSizePipe, TimeAgoPipe } from '../../shared/pipes/format.pipes';

type Tab = 'preview' | 'versions' | 'chunks';

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
    FileSizePipe,
    TimeAgoPipe,
  ],
  host: { class: 'block' },
  templateUrl: './document-detail.html',
  styles: `
    .dh-tab {
      position: relative;
      display: inline-flex;
      align-items: center;
      padding: 8px 12px 10px;
      font-size: 13px;
      font-weight: 500;
      color: var(--dh-text-subtle);
      transition: color 0.15s ease;
    }
    .dh-tab:hover {
      color: var(--dh-text);
    }
    .dh-tab-active {
      color: var(--dh-text);
    }
    .dh-tab-active::after {
      content: '';
      position: absolute;
      inset: auto 8px -1px 8px;
      height: 2px;
      border-radius: 2px 2px 0 0;
      background: linear-gradient(90deg, var(--dh-ai-from), var(--dh-ai-to));
    }

    /* The citation landing style — a wash plus a rail, never a hard border. */
    .dh-cited {
      background: color-mix(in oklab, var(--dh-ai-from) 11%, transparent);
      box-shadow: inset 3px 0 0 var(--dh-brand-500);
    }
  `,
})
export class DocumentDetailPage {
  private readonly gateway = inject(KnowledgeGateway);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly store = inject(LibraryStore);

  /** Bound from the route via `withComponentInputBinding()`. */
  readonly id = input<string>('');

  protected readonly tab = signal<Tab>('preview');
  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'preview', label: 'Preview' },
    { id: 'versions', label: 'Versions' },
    { id: 'chunks', label: 'Chunks' },
  ];

  private readonly result = toSignal(
    this.route.paramMap.pipe(
      map((params) => params.get('id') ?? ''),
      switchMap((id) => this.gateway.document(id)),
    ),
    { initialValue: undefined },
  );

  protected readonly doc = computed(() => this.result());
  protected readonly loading = computed(() => this.result() === undefined);

  /** `?chunk=N` — the citation deep-link contract, live from phase 1. */
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
      setTimeout(
        () =>
          document
            .getElementById(`chunk-${chunk}`)
            ?.scrollIntoView({ behavior: 'smooth', block: 'center' }),
        60,
      );
    });
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
