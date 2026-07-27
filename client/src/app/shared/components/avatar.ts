import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Person } from '../../core/models/knowledge.models';

@Component({
  selector: 'dh-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-grid shrink-0 place-items-center rounded-full font-semibold ring-1 select-none"
      [class]="sizeClass()"
      [style.color]="person().tint"
      [style.background]="'color-mix(in oklab, ' + person().tint + ' 18%, transparent)'"
      [style.--tw-ring-color]="'color-mix(in oklab, ' + person().tint + ' 34%, transparent)'"
      [attr.title]="person().name"
      [attr.aria-label]="person().name"
    >
      {{ person().initials }}
    </span>
  `,
})
export class Avatar {
  readonly person = input.required<Person>();
  readonly size = input<'xs' | 'sm' | 'md'>('sm');

  protected readonly sizeClass = computed(
    () =>
      ({
        xs: 'size-5 text-[9px]',
        sm: 'size-6 text-[10px]',
        md: 'size-8 text-[11px]',
      })[this.size()],
  );
}
