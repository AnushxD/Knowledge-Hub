import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Row-shaped skeletons — never a spinner, so layout doesn't jump on load. */
@Component({
  selector: 'dh-row-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (row of rows(); track $index) {
      <div class="flex items-center gap-3 border-b border-hairline px-4 py-3 last:border-0">
        <div class="dh-skeleton size-9 rounded-[10px]"></div>
        <div class="min-w-0 flex-1 space-y-2">
          <div
            class="dh-skeleton h-3 rounded"
            [style.width.%]="widths[$index % widths.length]"
          ></div>
          <div class="dh-skeleton h-2.5 w-1/4 rounded"></div>
        </div>
        <div class="dh-skeleton h-5 w-20 rounded-full"></div>
      </div>
    }
  `,
})
export class RowSkeleton {
  readonly count = input(6);
  protected readonly widths = [46, 62, 38, 54, 44, 58];
  protected rows() {
    return Array.from({ length: this.count() });
  }
}

@Component({
  selector: 'dh-card-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (card of cards(); track $index) {
      <div class="dh-card space-y-3 p-4">
        <div class="dh-skeleton size-12 rounded-[14px]"></div>
        <div class="dh-skeleton h-3 w-3/4 rounded"></div>
        <div class="dh-skeleton h-2.5 w-1/2 rounded"></div>
        <div class="dh-skeleton h-5 w-24 rounded-full"></div>
      </div>
    }
  `,
})
export class CardSkeleton {
  readonly count = input(8);
  protected cards() {
    return Array.from({ length: this.count() });
  }
}
