import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
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
  readonly doc = input.required<DocumentSummary>();

  readonly reindex = output<void>();
  readonly remove = output<void>();

  /** An item that needs nothing from the caller but closing the menu. */
  readonly dismiss = output<void>();
}
