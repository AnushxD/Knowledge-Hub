import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IngestionStatus } from '../../core/models/knowledge.models';

/**
 * The one visual vocabulary for ingestion state. Wherever a document appears
 * — list, grid, detail header, dashboard — it uses this component, so users
 * learn the four states once.
 */
@Component({
  selector: 'dh-status-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-flex items-center gap-1.5 rounded-full border px-2 py-[3px] text-[11px] font-medium whitespace-nowrap"
      [style.color]="meta().color"
      [style.borderColor]="'color-mix(in oklab, ' + meta().color + ' 32%, transparent)'"
      [style.background]="'color-mix(in oklab, ' + meta().color + ' 12%, transparent)'"
      [attr.title]="tooltip()"
    >
      @if (status() === 'indexing') {
        <span
          class="size-[7px] rounded-full"
          [style.background]="meta().color"
          style="animation: dh-pulse-ring 1.6s infinite"
        ></span>
      } @else if (status() === 'failed') {
        <i class="pi pi-exclamation-triangle text-[10px]"></i>
      } @else {
        <span class="size-[7px] rounded-full" [style.background]="meta().color"></span>
      }
      <span>{{ label() }}</span>
    </span>
  `,
})
export class StatusPill {
  readonly status = input.required<IngestionStatus>();
  /** 0–100 while indexing. */
  readonly progress = input<number | undefined>(undefined);

  protected readonly meta = computed(() => {
    switch (this.status()) {
      case 'indexed':
        return { color: 'var(--dh-status-indexed)', label: 'Indexed' };
      case 'indexing':
        return { color: 'var(--dh-status-indexing)', label: 'Indexing' };
      case 'failed':
        return { color: 'var(--dh-status-failed)', label: 'Failed' };
      default:
        return { color: 'var(--dh-status-pending)', label: 'Queued' };
    }
  });

  protected readonly label = computed(() => {
    const progress = this.progress();
    return this.status() === 'indexing' && progress != null
      ? `Indexing ${Math.round(progress)}%`
      : this.meta().label;
  });

  protected readonly tooltip = computed(() => {
    switch (this.status()) {
      case 'indexed':
        return 'Searchable, and the assistant can cite it.';
      case 'indexing':
        return 'Being extracted, chunked and embedded — not searchable yet.';
      case 'failed':
        return 'Ingestion failed. This document is invisible to search and the assistant.';
      default:
        return 'Uploaded and waiting for an ingestion worker.';
    }
  });
}
