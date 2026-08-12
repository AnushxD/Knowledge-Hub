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
  templateUrl: './status-pill.html',
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
        return 'Mirrored from the repository, waiting for an ingestion worker.';
    }
  });
}
