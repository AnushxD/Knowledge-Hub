import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  inject,
  signal,
} from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
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
  templateUrl: './shell.html',
})
export class Shell {
  private readonly palette = inject(CommandPaletteService);
  private readonly router = inject(Router);

  /** Route the assistant lives on, where its dock shortcut is redundant. */
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  protected readonly onAssistant = computed(() => this.url().startsWith('/chat'));

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
