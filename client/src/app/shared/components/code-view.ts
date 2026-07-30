import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Source text shown as source: monospace, whitespace preserved, gutter line
 * numbers.
 *
 * No syntax highlighting. That needs a grammar bundle per language, which is a
 * far larger dependency than the readability it would buy here — indentation
 * and line numbers are what make a SQL patch or a config file legible, and this
 * is a document hub rather than an editor.
 */
@Component({
  selector: 'dh-code-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dh-code">
      @for (line of lines(); track $index) {
        <div class="dh-code-line">
          <span class="dh-code-gutter" aria-hidden="true">{{ $index + 1 }}</span>
          <!-- A blank line still needs to occupy its row. -->
          <code>{{ line || ' ' }}</code>
        </div>
      }
    </div>
  `,
})
export class CodeView {
  readonly source = input.required<string>();

  protected readonly lines = computed(() =>
    // Normalise CRLF first, or every line on a Windows-authored file ends with
    // a stray carriage return that renders as a box glyph.
    this.source().replace(/\r\n?/g, '\n').split('\n'),
  );
}
