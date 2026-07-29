import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  output,
  signal,
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
