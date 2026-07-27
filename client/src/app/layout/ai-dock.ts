import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LibraryStore } from '../core/state/library-store';

/**
 * The assistant dock is *reserved* in phase 1, not implemented.
 *
 * It ships now, visibly inert, for two reasons: the layout never has to be
 * rebuilt when RAG lands in phase 3, and users learn where the assistant will
 * live. The scope chips also make the grounding model legible up front — the
 * assistant only ever answers from indexed content.
 */
@Component({
  selector: 'dh-ai-dock',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // `contents` keeps the host out of the shell's flex layout — everything it
  // renders is fixed-position anyway.
  host: { class: 'contents' },
  templateUrl: './ai-dock.html',
})
export class AiDock {
  private readonly store = inject(LibraryStore);
  protected readonly open = signal(false);
  protected readonly indexed = computed(() => this.store.stats()?.indexed ?? 0);
}
