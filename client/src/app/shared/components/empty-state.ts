import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Empty states are the onboarding surface of this product — every list has
 * one, and every one of them offers the next action rather than just saying
 * "no data".
 */
@Component({
  selector: 'dh-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './empty-state.html',
})
export class EmptyState {
  readonly icon = input('pi-inbox');
  readonly heading = input.required<string>();
  readonly message = input('');
}
