import { ChangeDetectionStrategy, Component, HostListener, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavRail } from './nav-rail';
import { TopBar } from './top-bar';
import { FolderTree } from './folder-tree';
import { AiDock } from './ai-dock';
import { CommandPalette } from './command-palette';
import { CommandPaletteService } from './command-palette.service';

/**
 * Application frame: icon rail → contextual sidebar → content, with the
 * assistant docked on the right. Every screen renders inside this, so chrome
 * never re-mounts on navigation.
 */
@Component({
  selector: 'dh-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NavRail, TopBar, FolderTree, AiDock, CommandPalette],
  host: { class: 'flex h-dvh overflow-hidden' },
  template: `
    <dh-nav-rail class="hidden sm:flex" />

    <!-- Sidebar: docked on desktop, an overlay drawer below lg. -->
    @if (sidebarOpen()) {
      <div
        class="fixed inset-0 z-30 bg-black/40 lg:hidden"
        (click)="sidebarOpen.set(false)"
        aria-hidden="true"
      ></div>
    }
    <aside
      class="fixed inset-y-0 left-0 z-40 flex w-[var(--dh-sidebar-w)] shrink-0 flex-col border-r border-hairline bg-surface-1 transition-transform duration-200 lg:static lg:z-auto lg:translate-x-0"
      [class.-translate-x-full]="!sidebarOpen()"
      [class.translate-x-0]="sidebarOpen()"
      [class.lg:hidden]="!desktopSidebar()"
      aria-label="Library navigation"
    >
      <div
        class="flex h-[var(--dh-topbar-h)] shrink-0 items-center gap-2 border-b border-hairline px-4"
      >
        <span class="text-[14px] font-semibold tracking-tight text-ink">DocHub</span>
        <span
          class="rounded-full border border-hairline px-1.5 py-px text-[9.5px] font-medium text-subtle"
          >Phase 1</span
        >
        <button
          type="button"
          class="ml-auto grid size-7 place-items-center rounded-lg text-subtle transition hover:bg-surface-2 hover:text-ink"
          (click)="collapseSidebar()"
          aria-label="Collapse sidebar"
        >
          <i class="pi pi-angle-double-left text-[13px]"></i>
        </button>
      </div>
      <dh-folder-tree class="min-h-0 flex-1" />
    </aside>

    <div class="flex min-w-0 flex-1 flex-col">
      <dh-top-bar (toggleSidebar)="toggleSidebar()" />
      <!-- pb clears the assistant FAB so it never covers the last row. -->
      <main class="min-h-0 flex-1 overflow-y-auto pb-24">
        <router-outlet />
      </main>
    </div>

    <dh-ai-dock />
    <dh-command-palette />
  `,
})
export class Shell {
  private readonly palette = inject(CommandPaletteService);

  protected readonly sidebarOpen = signal(false);
  /** Whether the sidebar occupies space on large screens. */
  protected readonly desktopSidebar = signal(true);

  protected toggleSidebar(): void {
    if (window.innerWidth >= 1024) {
      this.desktopSidebar.update((v) => !v);
    } else {
      this.sidebarOpen.update((v) => !v);
    }
  }

  protected collapseSidebar(): void {
    if (window.innerWidth >= 1024) this.desktopSidebar.set(false);
    else this.sidebarOpen.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.palette.toggle();
      return;
    }
    if (event.key === 'Escape') this.palette.hide();
    // `/` focuses search, the convention every doc tool shares.
    if (event.key === '/' && !this.isTypingTarget(event.target)) {
      event.preventDefault();
      this.palette.show();
    }
  }

  private isTypingTarget(target: EventTarget | null): boolean {
    const el = target as HTMLElement | null;
    return !!el && ['INPUT', 'TEXTAREA'].includes(el.tagName);
  }
}
