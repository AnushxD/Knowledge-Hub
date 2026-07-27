import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router } from '@angular/router';
import { LibraryStore } from '../core/state/library-store';
import { Folder } from '../core/models/knowledge.models';
import { formatBytes } from '../core/utils/file-kind';

@Component({
  selector: 'dh-folder-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  host: { class: 'flex min-h-0 flex-col' },
  templateUrl: './folder-tree.html',
  styleUrl: './folder-tree.css',
})
export class FolderTree {
  protected readonly store = inject(LibraryStore);
  private readonly router = inject(Router);

  protected readonly expanded = signal(new Set<string>(['f-eng', 'f-onb']));

  protected readonly roots = computed(() =>
    (this.store.folders() ?? []).filter((f) => f.parentId === null),
  );

  protected readonly isAll = computed(
    () => this.store.folderId() === null && !this.store.starredOnly() && !this.onlyFailed(),
  );

  protected readonly onlyFailed = computed(
    () => this.store.statuses().length === 1 && this.store.statuses()[0] === 'failed',
  );

  protected readonly storagePercent = computed(() => {
    const used = this.store.stats()?.storageBytes ?? 0;
    return Math.min(100, Math.round((used / 50_000_000) * 100));
  });

  protected readonly storageLabel = computed(
    () => `${formatBytes(this.store.stats()?.storageBytes ?? 0)} of 50 MB`,
  );

  protected childrenOf(id: string) {
    return (this.store.folders() ?? []).filter((f) => f.parentId === id);
  }

  protected hasChildren(folder: Folder): boolean {
    return this.childrenOf(folder.id).length > 0;
  }

  protected toggle(id: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  protected expand(id: string): void {
    if (!this.expanded().has(id)) this.toggle(id);
  }

  protected collapse(id: string): void {
    if (this.expanded().has(id)) this.toggle(id);
  }

  protected open(folder: Folder): void {
    this.store.clearFilters();
    this.store.openFolder(folder.id);
    if (this.hasChildren(folder) && !this.expanded().has(folder.id)) this.toggle(folder.id);
    this.router.navigate(['/browse']);
  }

  protected openAll(): void {
    this.store.clearFilters();
    this.store.openFolder(null);
    this.router.navigate(['/browse']);
  }

  protected openStarred(): void {
    this.store.showStarred();
    this.router.navigate(['/browse']);
  }

  protected openFailed(): void {
    this.store.showOnlyFailed();
    this.router.navigate(['/browse']);
  }

  protected createFolder(parentId: string | null): void {
    const name = prompt('Folder name');
    if (!name?.trim()) return;
    this.store.createFolder(parentId, name.trim());
    if (parentId) this.expanded.update((set) => new Set(set).add(parentId));
  }
}
