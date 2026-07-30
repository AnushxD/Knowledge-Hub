import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router } from '@angular/router';
import { LibraryStore } from '../core/state/library-store';
import { AuthStore } from '../core/state/auth-store';
import { Folder } from '../core/models/knowledge.models';
import { formatBytes } from '../core/utils/file-kind';
import { ConfirmDialog } from '../shared/components/confirm-dialog';

@Component({
  selector: 'dh-folder-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, ConfirmDialog],
  host: { class: 'flex min-h-0 flex-col' },
  templateUrl: './folder-tree.html',
  styleUrl: './folder-tree.css',
})
export class FolderTree {
  protected readonly store = inject(LibraryStore);
  protected readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  /** The folder awaiting a delete confirmation, if any. */
  protected readonly pendingDelete = signal<Folder | null>(null);

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

  /**
   * What the confirmation spells out.
   *
   * `documentCount` is recursive, so it already covers the subtree — which is
   * the number that matters, because the whole subtree goes.
   */
  protected deleteMessage(folder: Folder): string {
    const subfolders = this.descendantCount(folder.id);
    const documents = folder.documentCount;

    const parts = [
      documents === 1 ? '1 document' : `${documents} documents`,
      subfolders === 1 ? '1 subfolder' : `${subfolders} subfolders`,
    ];

    return subfolders > 0
      ? `“${folder.name}” holds ${parts[0]} across ${parts[1]}.`
      : `“${folder.name}” holds ${parts[0]}.`;
  }

  private descendantCount(id: string): number {
    const children = this.childrenOf(id);
    return children.length + children.reduce((sum, c) => sum + this.descendantCount(c.id), 0);
  }

  protected confirmDelete(): void {
    const folder = this.pendingDelete();
    if (!folder) return;

    this.store.deleteFolder(folder.id);
    this.pendingDelete.set(null);

    // The subtree is gone; leaving its ids expanded would keep stale rows open
    // if a folder with the same id ever came back.
    this.expanded.update((set) => {
      const next = new Set(set);
      next.delete(folder.id);
      return next;
    });
  }
}
