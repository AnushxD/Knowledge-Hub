import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Person } from '../../core/models/knowledge.models';

@Component({
  selector: 'dh-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './avatar.html',
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
