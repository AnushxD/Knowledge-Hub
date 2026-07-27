import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { FileKind } from '../../core/models/knowledge.models';
import { presentationFor } from '../../core/utils/file-kind';

@Component({
  selector: 'dh-file-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './file-icon.html',
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
