import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Citation } from '../../core/models/knowledge.models';
import { TooltipDirective } from '../../shared/directives/tooltip.directive';

/** A run of answer text, or a resolved citation marker. */
interface Part {
  text: string;
  citation?: Citation;
}

/**
 * Renders an answer with its `[1]` markers turned into links to the exact
 * passage they came from.
 *
 * Markers are matched against the verified citation list rather than trusted
 * from the text: the server already dropped any the model invented, and
 * anything that still fails to resolve is rendered as plain text instead of a
 * link that goes nowhere.
 */
@Component({
  selector: 'dh-citation-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TooltipDirective],
  template: `<p
    class="text-[13.5px] leading-[1.7] whitespace-pre-wrap text-ink"
  >@for (part of parts(); track $index) {@if (part.citation; as citation) {<a
        class="dh-citation"
        [routerLink]="['/docs', citation.documentId]"
        [queryParams]="{ chunk: citation.chunkId }"
        [dhTooltip]="citation.documentTitle + ' · ' + citation.heading"
      >{{ citation.marker }}</a>} @else {{{ part.text }}}}</p>`,
})
export class CitationText {
  readonly content = input.required<string>();
  readonly citations = input.required<Citation[]>();

  protected readonly parts = computed<Part[]>(() => {
    const text = this.content();
    const byMarker = new Map(this.citations().map((c) => [c.marker, c]));
    const parts: Part[] = [];

    const pattern = /\[(\d{1,3})\]/g;
    let lastIndex = 0;

    for (const match of text.matchAll(pattern)) {
      const index = match.index ?? 0;
      const citation = byMarker.get(Number(match[1]));

      // An unresolved marker stays as literal text — never a dead link.
      if (!citation) continue;

      if (index > lastIndex) parts.push({ text: text.slice(lastIndex, index) });
      parts.push({ text: match[0], citation });
      lastIndex = index + match[0].length;
    }

    if (lastIndex < text.length) parts.push({ text: text.slice(lastIndex) });

    return parts;
  });
}
