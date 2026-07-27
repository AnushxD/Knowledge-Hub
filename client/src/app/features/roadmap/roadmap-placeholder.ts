import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Presentational shell for a reserved route.
 *
 * The roadmap in CLAUDE.md is deliberate — search lands in phase 2, the
 * assistant in phase 3, MCP sources in phase 4 — so those screens exist as
 * designed placeholders rather than half-built features. They keep the
 * navigation model honest and document the intent for whoever builds them.
 */
@Component({
  selector: 'dh-roadmap-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  host: { class: 'block' },
  templateUrl: './roadmap-placeholder.html',
})
export class RoadmapPlaceholder {
  readonly phase = input.required<number>();
  readonly heading = input.required<string>();
  readonly blurb = input.required<string>();
  readonly features = input.required<{ icon: string; title: string; detail: string }[]>();
  readonly dependencies = input.required<string[]>();
}
