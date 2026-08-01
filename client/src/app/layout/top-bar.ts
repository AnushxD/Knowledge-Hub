import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TooltipDirective } from '../shared/directives/tooltip.directive';
import { CommandPaletteService } from './command-palette.service';
import { Avatar } from '../shared/components/avatar';
import { Person } from '../core/models/knowledge.models';
import { AuthStore } from '../core/state/auth-store';

@Component({
  selector: 'dh-top-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TooltipDirective, Avatar],
  host: {
    class:
      'sticky top-0 z-30 flex h-[var(--dh-topbar-h)] shrink-0 items-center gap-3 border-b border-hairline px-3 dh-glass',
    // Listened for on the document rather than caught by a full-screen
    // backdrop. A backdrop was the obvious approach and did not work: this
    // host carries `dh-glass`, whose `backdrop-filter` makes it the containing
    // block for `position: fixed` children, so an `inset-0` overlay covered
    // the top bar strip instead of the viewport and every click below it
    // missed.
    '(document:pointerdown)': 'closeMenuOnOutsideClick($event)',
    '(document:keydown.escape)': 'menuOpen.set(false)',
  },
  templateUrl: './top-bar.html',
  styleUrl: './top-bar.css',
})
export class TopBar {
  protected readonly palette = inject(CommandPaletteService);
  readonly toggleSidebar = output<void>();

  protected readonly auth = inject(AuthStore);

  /**
   * The signed-in principal, shaped as the avatar component expects. The tint
   * is derived from the id so a person keeps the same colour everywhere,
   * without the server having to have an opinion about colours.
   */
  protected readonly currentUser = computed<Person>(() => {
    const user = this.auth.currentUser();

    return {
      id: user?.id ?? 'anonymous',
      name: user?.name ?? 'Signed out',
      initials: user?.initials ?? '?',
      tint: TINTS[hash(user?.id ?? '') % TINTS.length],
    };
  });

  protected readonly menuOpen = signal(false);

  /** The button and the menu together — a click inside either is not "outside". */
  private readonly account = viewChild.required<ElementRef<HTMLElement>>('account');

  /**
   * `pointerdown` rather than `click`, so the menu is gone by the time whatever
   * was clicked reacts, and so a click that removes its own target still
   * closes it.
   */
  protected closeMenuOnOutsideClick(event: Event): void {
    if (!this.menuOpen()) return;

    if (!this.account().nativeElement.contains(event.target as Node)) {
      this.menuOpen.set(false);
    }
  }

  protected signOut(): void {
    this.menuOpen.set(false);
    this.auth.signOut();
  }
}

const TINTS = ['#7c5cff', '#22d3ee', '#f472b6', '#10b981', '#f97316'];

const hash = (value: string): number => {
  let total = 0;
  for (const character of value) total = (total + character.charCodeAt(0)) % 997;
  return total;
};
