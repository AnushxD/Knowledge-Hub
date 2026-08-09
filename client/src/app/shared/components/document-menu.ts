import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { DocumentSummary } from '../../core/models/knowledge.models';

/**
 * The per-document action menu, shared by the library's list and grid views.
 *
 * One component rather than one per view: a document that can be re-indexed
 * from a row but not from a card would make the same object more or less
 * manageable depending on a display toggle, which is not a distinction the user
 * asked for.
 *
 * `display: contents` on the host so the panel positions against whatever the
 * caller made `relative` — the row's action cluster or the card's — rather than
 * against a wrapper this component would otherwise introduce.
 *
 * Open state deliberately stays with the caller: "only one menu open at a time"
 * is a property of the list, not of any single document.
 */
@Component({
  selector: 'dh-document-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  host: { class: 'contents' },
  templateUrl: './document-menu.html',
})
export class DocumentMenu {
  private readonly gateway = inject(KnowledgeGateway);

  readonly doc = input.required<DocumentSummary>();

  /**
   * Null when the gateway has no stored file, which drops the item rather than
   * showing one that does nothing.
   *
   * The gateway is injected here rather than passed in: the URL is the client's
   * one sanctioned way to reach the API, and threading it through every caller
   * would put that knowledge in the list screens instead.
   */
  protected readonly downloadUrl = computed(() =>
    this.gateway.documentDownloadUrl(this.doc().id),
  );

  readonly reindex = output<void>();

  /** An item that needs nothing from the caller but closing the menu. */
  readonly dismiss = output<void>();
}
