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
  template: `
    <!-- Brand mark doubles as the "go home" affordance. -->
    <a
      routerLink="/"
      class="mb-2 grid size-10 place-items-center rounded-[14px] text-white shadow-glow transition-transform hover:scale-105"
      style="background: linear-gradient(135deg, var(--dh-ai-from), var(--dh-ai-to))"
      aria-label="DocHub home"
    >
      <i class="pi pi-sparkles text-[17px]"></i>
    </a>

    <nav class="flex flex-1 flex-col items-center gap-1" aria-label="Primary">
      @for (item of items; track item.route) {
        <a
          [routerLink]="item.route"
          routerLinkActive="dh-rail-active"
          [routerLinkActiveOptions]="{ exact: !!item.exact }"
          [dhTooltip]="item.phase ? item.label + ' · Phase ' + item.phase : item.label"
          tooltipPosition="right"
          class="dh-rail-item"
          [attr.aria-label]="item.label"
        >
          <i class="pi text-[17px]" [class]="item.icon"></i>
          @if (item.phase) {
            <span
              class="absolute top-1.5 right-1.5 size-1.5 rounded-full bg-brand-400/70"
              aria-hidden="true"
            ></span>
          }
        </a>
      }
    </nav>

    <button
      type="button"
      class="dh-rail-item"
      (click)="theme.toggle()"
      [dhTooltip]="theme.mode() === 'dark' ? 'Switch to light' : 'Switch to dark'"
      tooltipPosition="right"
      [attr.aria-label]="theme.mode() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'"
    >
      <i class="pi text-[16px]" [class]="theme.mode() === 'dark' ? 'pi-sun' : 'pi-moon'"></i>
    </button>
  `,
  styles: `
    .dh-rail-item {
      position: relative;
      display: grid;
      place-items: center;
      width: 42px;
      height: 42px;
      border-radius: 13px;
      color: var(--dh-text-subtle);
      transition:
        background 0.16s ease,
        color 0.16s ease;
    }
    .dh-rail-item:hover {
      background: var(--dh-surface-2);
      color: var(--dh-text);
    }
    .dh-rail-item.dh-rail-active {
      background: color-mix(in oklab, var(--dh-brand-500) 16%, transparent);
      color: var(--dh-brand-400);
    }
    /* Active indicator bar on the rail edge. */
    .dh-rail-item.dh-rail-active::before {
      content: '';
      position: absolute;
      left: -13px;
      width: 3px;
      height: 20px;
      border-radius: 0 3px 3px 0;
      background: linear-gradient(var(--dh-ai-from), var(--dh-ai-to));
    }
  `,
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
