import { Directive, ElementRef, OnDestroy, inject, input } from '@angular/core';

type Position = 'top' | 'bottom' | 'left' | 'right';

/**
 * Minimal tooltip. Appends a single positioned node to <body> on hover/focus so
 * it is never clipped by an ancestor's overflow, and removes it on leave.
 */
@Directive({
  selector: '[dhTooltip]',
  host: {
    '(mouseenter)': 'show()',
    '(mouseleave)': 'hide()',
    '(focusin)': 'show()',
    '(focusout)': 'hide()',
    '(click)': 'hide()',
  },
})
export class TooltipDirective implements OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly dhTooltip = input<string | undefined | null>('');
  readonly tooltipPosition = input<Position>('top');

  private node?: HTMLElement;

  protected show(): void {
    const text = this.dhTooltip();
    if (!text || this.node) return;

    const node = document.createElement('div');
    node.textContent = text;
    node.setAttribute('role', 'tooltip');
    node.className = 'dh-tooltip';
    document.body.appendChild(node);
    this.node = node;

    const anchor = (this.host.nativeElement as HTMLElement).getBoundingClientRect();
    const tip = node.getBoundingClientRect();
    const gap = 8;

    let top: number;
    let left: number;
    switch (this.tooltipPosition()) {
      case 'right':
        top = anchor.top + anchor.height / 2 - tip.height / 2;
        left = anchor.right + gap;
        break;
      case 'left':
        top = anchor.top + anchor.height / 2 - tip.height / 2;
        left = anchor.left - tip.width - gap;
        break;
      case 'bottom':
        top = anchor.bottom + gap;
        left = anchor.left + anchor.width / 2 - tip.width / 2;
        break;
      default:
        top = anchor.top - tip.height - gap;
        left = anchor.left + anchor.width / 2 - tip.width / 2;
    }

    // Keep it inside the viewport.
    left = Math.max(8, Math.min(left, window.innerWidth - tip.width - 8));
    top = Math.max(8, Math.min(top, window.innerHeight - tip.height - 8));

    node.style.top = `${top}px`;
    node.style.left = `${left}px`;
    requestAnimationFrame(() => node.classList.add('dh-tooltip-in'));
  }

  protected hide(): void {
    this.node?.remove();
    this.node = undefined;
  }

  ngOnDestroy(): void {
    this.hide();
  }
}
