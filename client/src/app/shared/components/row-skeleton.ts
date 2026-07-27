import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Row-shaped skeleton — never a spinner, so layout doesn't jump on load. */
@Component({
  selector: 'dh-row-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './row-skeleton.html',
})
export class RowSkeleton {
  readonly count = input(6);
  protected readonly widths = [46, 62, 38, 54, 44, 58];
  protected rows() {
    return Array.from({ length: this.count() });
  }
}
