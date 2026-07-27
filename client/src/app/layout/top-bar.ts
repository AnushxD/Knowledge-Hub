import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TooltipDirective } from '../shared/directives/tooltip.directive';
import { CommandPaletteService } from './command-palette.service';
import { Avatar } from '../shared/components/avatar';
import { Person } from '../core/models/knowledge.models';

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

  /** Replaced by the authenticated principal in phase 5. */
  protected readonly currentUser: Person = {
    id: 'u1',
    name: 'Ana Ruiz',
    initials: 'AR',
    tint: '#7c5cff',
  };
}
