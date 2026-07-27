import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Card-shaped skeleton, used by the library's grid view. */
@Component({
  selector: 'dh-card-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './card-skeleton.html',
})
export class CardSkeleton {
  readonly count = input(8);
  protected cards() {
    return Array.from({ length: this.count() });
  }
}
