import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LibraryStore } from '../../core/state/library-store';
import { AuthStore } from '../../core/state/auth-store';
import { FileIcon } from '../../shared/components/file-icon';
import { StatusPill } from '../../shared/components/status-pill';
import { Avatar } from '../../shared/components/avatar';
import { RowSkeleton } from '../../shared/components/row-skeleton';
import { FileSizePipe, TimeAgoPipe } from '../../shared/pipes/format.pipes';
import { ActivityEvent } from '../../core/models/knowledge.models';

@Component({
  selector: 'dh-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, FileIcon, StatusPill, Avatar, RowSkeleton, FileSizePipe, TimeAgoPipe],
  host: { class: 'block' },
  templateUrl: './dashboard.html',
})
export class Dashboard {
  protected readonly store = inject(LibraryStore);
  private readonly auth = inject(AuthStore);

  /** Greets by first name; falls back to something neutral before /auth/me answers. */
  protected readonly firstName = computed(
    () => this.auth.currentUser()?.name.trim().split(' ')[0] || 'there',
  );
  protected readonly stats = this.store.stats;
  protected readonly activity = this.store.activity;

  protected readonly today = new Date().toLocaleDateString(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  });

  protected greeting(): string {
    const hour = new Date().getHours();
    if (hour < 12) return 'Good morning';
    if (hour < 18) return 'Good afternoon';
    return 'Good evening';
  }

  protected readonly recent = computed(() => (this.store.documents() ?? []).slice(0, 6));

  protected readonly tiles = computed(() => {
    const s = this.stats();
    return [
      {
        label: 'Documents',
        value: s?.documents ?? '—',
        hint: `across ${s?.folders ?? 0} folders`,
        icon: 'pi-file',
        tint: 'var(--dh-brand-500)',
      },
      {
        label: 'Indexed',
        value: s?.indexed ?? '—',
        hint: `${s?.chunks ?? 0} retrievable chunks`,
        icon: 'pi-check-circle',
        tint: 'var(--dh-status-indexed)',
      },
      {
        label: 'In pipeline',
        value: s?.indexing ?? '—',
        hint: 'queued or embedding',
        icon: 'pi-sync',
        tint: 'var(--dh-status-indexing)',
      },
      {
        label: 'Storage used',
        value: this.compactBytes(s?.contentBytes ?? 0),
        hint: 'Azurite locally, Blob in prod',
        icon: 'pi-database',
        tint: 'var(--dh-status-pending)',
      },
    ];
  });

  protected readonly segments = computed(() => {
    const s = this.stats();
    const total = Math.max(1, s?.documents ?? 1);
    return [
      {
        label: 'Indexed',
        count: s?.indexed ?? 0,
        percent: ((s?.indexed ?? 0) / total) * 100,
        tint: 'var(--dh-status-indexed)',
        icon: 'pi-check-circle',
      },
      {
        label: 'In pipeline',
        count: s?.indexing ?? 0,
        percent: ((s?.indexing ?? 0) / total) * 100,
        tint: 'var(--dh-status-indexing)',
        icon: 'pi-sync',
      },
      {
        label: 'Failed',
        count: s?.failed ?? 0,
        percent: ((s?.failed ?? 0) / total) * 100,
        tint: 'var(--dh-status-failed)',
        icon: 'pi-exclamation-triangle',
      },
    ];
  });

  /**
   * Reads naturally after either an actor or "The repository", which is what
   * the two halves of this feed are now.
   */
  protected verb(event: ActivityEvent): string {
    switch (event.type) {
      case 'added':
        return 'added';
      case 'changed':
        return 'changed';
      case 'removed':
        return 'removed';
      case 'indexed':
        return 'finished indexing';
      case 'failed':
        return 'hit an ingestion failure on';
      case 'synced':
        return 'synced';
      default:
        return 'updated';
    }
  }

  private compactBytes(bytes: number): string {
    if (bytes >= 1_000_000_000) return `${(bytes / 1_073_741_824).toFixed(1)} GB`;
    if (bytes >= 1_000_000) return `${Math.round(bytes / 1_048_576)} MB`;
    return `${Math.round(bytes / 1024)} KB`;
  }
}
