import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TooltipDirective } from '../shared/directives/tooltip.directive';
import { ThemeService } from '../core/theme/theme.service';

interface RailItem {
  route: string;
  icon: string;
  label: string;
  /** Roadmap phase that lights this section up. Undefined = available now. */
  phase?: number;
  exact?: boolean;
}

@Component({
  selector: 'dh-nav-rail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, TooltipDirective],
  host: {
    class:
      'flex h-full w-[var(--dh-rail-w)] shrink-0 flex-col items-center gap-1 border-r border-hairline bg-surface-1 py-3',
  },
  templateUrl: './nav-rail.html',
  styleUrl: './nav-rail.css',
})
export class NavRail {
  protected readonly theme = inject(ThemeService);

  protected readonly items: RailItem[] = [
    { route: '/', icon: 'pi-home', label: 'Home', exact: true },
    { route: '/browse', icon: 'pi-folder', label: 'Library' },
    { route: '/search', icon: 'pi-search', label: 'Search', phase: 2 },
    { route: '/chat', icon: 'pi-comments', label: 'Assistant', phase: 3 },
    { route: '/sources', icon: 'pi-share-alt', label: 'Knowledge sources', phase: 4 },
    { route: '/settings', icon: 'pi-cog', label: 'Settings' },
  ];
}
