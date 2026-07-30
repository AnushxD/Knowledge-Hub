import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  inject,
  input,
  output,
} from '@angular/core';

/**
 * A confirmation for something that cannot be undone.
 *
 * Its own component rather than `window.confirm`, for two reasons beyond looks:
 * a native dialog cannot say *what* is about to be lost, and it renders as a
 * browser chrome alert that people dismiss without reading. The consequence
 * belongs on screen, in the product's own voice.
 *
 * The confirming action is never the default focus — the reader should have to
 * reach for it.
 */
@Component({
  selector: 'dh-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    // Escape cancels, as in every dialog. Bound on the document rather than the
    // panel so it works before anything inside has been focused.
    '(document:keydown.escape)': 'cancel.emit()',
  },
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog implements AfterViewInit, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);

  /**
   * Moves the dialog to the body so the overlay covers the window rather than
   * whatever it happened to be declared inside.
   *
   * `position: fixed` is viewport-relative only while no ancestor creates a
   * containing block, and the sidebar does: it slides with `translate`, which
   * counts even at `translate: 0px`. A dialog opened from the folder tree was
   * therefore laid out inside the 273px sidebar. Relocating keeps the component
   * usable from anywhere without every caller having to know that.
   *
   * In `ngAfterViewInit`, not the constructor: Angular inserts the host element
   * *after* constructing the component, so moving it any earlier is undone a
   * moment later. Angular still owns the view either way, so bindings and
   * outputs are unaffected.
   */
  ngAfterViewInit(): void {
    document.body.appendChild(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.host.nativeElement.remove();
  }

  readonly heading = input.required<string>();

  /** What will happen, in one or two plain sentences. */
  readonly message = input.required<string>();

  /** The consequence worth spelling out on its own, when there is one. */
  readonly detail = input<string>('');

  readonly confirmLabel = input('Delete');

  readonly confirm = output<void>();
  readonly cancel = output<void>();
}
