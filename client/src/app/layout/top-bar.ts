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
  template: `
    <button
      type="button"
      class="dh-icon-btn"
      (click)="toggleSidebar.emit()"
      aria-label="Toggle navigation"
    >
      <i class="pi pi-bars text-[15px]"></i>
    </button>

    <!-- One omnibox for both "find a document" and (from phase 3) "ask a
         question". Clicking it opens the command palette. -->
    <button
      type="button"
      class="group flex h-9 min-w-0 flex-1 items-center gap-2.5 rounded-dh border border-hairline bg-surface-2/70 px-3 text-left transition hover:border-hairline-strong hover:bg-surface-2"
      (click)="palette.show()"
    >
      <i class="pi pi-search shrink-0 text-[13px] text-subtle"></i>
      <span class="truncate text-[13px] text-subtle">Search documents or ask a question…</span>
      <kbd
        class="ml-auto hidden shrink-0 rounded-md border border-hairline bg-surface-1 px-1.5 py-0.5 font-sans text-[10.5px] font-medium text-subtle sm:block"
      >
        ⌘K
      </kbd>
    </button>

    <div class="flex shrink-0 items-center gap-1">
      <a
        routerLink="/browse"
        [queryParams]="{ upload: 1 }"
        class="hidden h-9 items-center gap-2 rounded-dh px-3.5 text-[13px] font-medium text-white transition hover:brightness-110 sm:flex"
        style="background: linear-gradient(135deg, var(--dh-brand-600), var(--dh-brand-500))"
      >
        <i class="pi pi-upload text-[13px]"></i>
        Upload
      </a>

      <button
        type="button"
        class="dh-icon-btn relative"
        dhTooltip="Notifications"
        tooltipPosition="bottom"
        aria-label="Notifications"
      >
        <i class="pi pi-bell text-[15px]"></i>
        <span class="absolute top-2 right-2.5 size-1.5 rounded-full bg-status-failed"></span>
      </button>

      <button
        type="button"
        class="ml-1 rounded-full transition hover:opacity-85"
        [dhTooltip]="currentUser.name"
        tooltipPosition="bottom"
      >
        <dh-avatar [person]="currentUser" size="md" />
      </button>
    </div>
  `,
  styles: `
    .dh-icon-btn {
      display: grid;
      place-items: center;
      width: 36px;
      height: 36px;
      border-radius: 10px;
      color: var(--dh-text-muted);
      transition:
        background 0.16s ease,
        color 0.16s ease;
    }
    .dh-icon-btn:hover {
      background: var(--dh-surface-2);
      color: var(--dh-text);
    }
  `,
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
