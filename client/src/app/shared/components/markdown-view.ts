import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { marked } from 'marked';

/**
 * Renders Markdown as Markdown.
 *
 * Bound through `[innerHTML]` rather than `DomSanitizer.bypassSecurityTrustHtml`,
 * and that is the whole security argument: an `[innerHTML]` binding runs
 * Angular's sanitizer, which drops `<script>`, `on*` handlers and
 * `javascript:` URLs. Documents here are uploaded by contributors, so the
 * Markdown is untrusted input — bypassing the sanitizer would turn any upload
 * into stored XSS against every reader.
 *
 * `marked` is a parser, not a UI kit, so it does not run into the licensing
 * problem that ruled out a component library.
 */
@Component({
  selector: 'dh-markdown-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="dh-prose" [innerHTML]="html()"></div>`,
})
export class MarkdownView {
  readonly source = input.required<string>();

  protected readonly html = computed(
    () =>
      marked.parse(this.source(), {
        async: false,
        // GitHub-flavoured: tables, strikethrough and fenced code are what
        // internal documentation actually uses.
        gfm: true,
        // A single newline stays a soft wrap. Documentation is usually written
        // hard-wrapped at 80 columns, and honouring those as <br> would shred
        // every paragraph.
        breaks: false,
      }) as string,
  );
}
