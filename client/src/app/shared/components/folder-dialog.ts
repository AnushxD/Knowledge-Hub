import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';

/** Mirrors what the API enforces, so a name is refused here before a round trip. */
const MAX_NAME_LENGTH = 200;

/**
 * Names a new folder.
 *
 * Replaces `window.prompt`, which could not say where the folder was going,
 * could not show the rules until the server rejected the name, and is styled by
 * the browser rather than the product.
 *
 * The validation deliberately repeats the server's: `/` is the separator for
 * the materialised path, so a name containing one would corrupt every
 * descendant's path. Checking here is a courtesy that saves a round trip — the
 * API still enforces it, and this could be bypassed entirely.
 */
@Component({
  selector: 'dh-folder-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'cancel.emit()' },
  templateUrl: './folder-dialog.html',
})
export class FolderDialog implements AfterViewInit, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');

  /** Where the folder will be created, for the subtitle. Empty means top level. */
  readonly parentName = input('');

  readonly name = signal('');

  protected readonly trimmed = computed(() => this.name().trim());

  /**
   * Null while the name is acceptable *or* still untouched — a form that scolds
   * you before you have typed anything is worse than one that waits.
   */
  protected readonly error = computed(() => {
    const value = this.trimmed();

    if (value.length === 0) return null;
    if (value.includes('/')) return 'A folder name cannot contain “/”.';
    if (value.length > MAX_NAME_LENGTH) return `Keep it under ${MAX_NAME_LENGTH} characters.`;

    return null;
  });

  protected readonly canSubmit = computed(() => this.trimmed().length > 0 && !this.error());

  readonly create = output<string>();
  readonly cancel = output<void>();

  /** See `ConfirmDialog` — the sidebar's `translate` traps anything fixed inside it. */
  ngAfterViewInit(): void {
    document.body.appendChild(this.host.nativeElement);
    this.field()?.nativeElement.focus();
  }

  ngOnDestroy(): void {
    this.host.nativeElement.remove();
  }

  protected onInput(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected submit(): void {
    if (!this.canSubmit()) return;
    this.create.emit(this.trimmed());
  }
}
