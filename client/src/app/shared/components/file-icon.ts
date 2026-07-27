import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { FileKind } from '../../core/models/knowledge.models';
import { presentationFor } from '../../core/utils/file-kind';

@Component({
  selector: 'dh-file-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-grid shrink-0 place-items-center rounded-[10px] border"
      [class]="sizeClass()"
      [style.color]="tint()"
      [style.borderColor]="'color-mix(in oklab, ' + tint() + ' 26%, transparent)'"
      [style.background]="
        'linear-gradient(150deg, color-mix(in oklab, ' +
        tint() +
        ' 20%, transparent), color-mix(in oklab, ' +
        tint() +
        ' 7%, transparent))'
      "
    >
      <i class="pi" [class]="icon()"></i>
    </span>
  `,
})
export class FileIcon {
  readonly kind = input.required<FileKind>();
  readonly size = input<'sm' | 'md' | 'lg'>('md');

  protected readonly tint = computed(() => presentationFor(this.kind()).tint);
  protected readonly icon = computed(() => presentationFor(this.kind()).icon);
  protected readonly sizeClass = computed(
    () =>
      ({
        sm: 'size-7 text-[12px]',
        md: 'size-9 text-[15px]',
        lg: 'size-12 text-[20px] rounded-[14px]',
      })[this.size()],
  );
}
