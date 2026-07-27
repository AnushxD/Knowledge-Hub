import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Empty states are the onboarding surface of this product — every list has
 * one, and every one of them offers the next action rather than just saying
 * "no data".
 */
@Component({
  selector: 'dh-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dh-aurora flex flex-col items-center justify-center px-6 py-16 text-center">
      <div
        class="dh-ai-surface mb-5 grid size-16 place-items-center rounded-[20px] text-2xl text-brand-400"
      >
        <i class="pi" [class]="icon()"></i>
      </div>
      <h3 class="text-[15px] font-semibold text-ink">{{ heading() }}</h3>
      <p class="mt-1.5 max-w-md text-[13px] leading-relaxed text-muted">{{ message() }}</p>
      <div class="mt-5 flex flex-wrap items-center justify-center gap-2">
        <ng-content />
      </div>
    </div>
  `,
})
export class EmptyState {
  readonly icon = input('pi-inbox');
  readonly heading = input.required<string>();
  readonly message = input('');
}
