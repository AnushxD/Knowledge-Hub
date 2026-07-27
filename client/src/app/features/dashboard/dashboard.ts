import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LibraryStore } from '../../core/state/library-store';
import { FileIcon } from '../../shared/components/file-icon';
import { StatusPill } from '../../shared/components/status-pill';
import { Avatar } from '../../shared/components/avatar';
import { RowSkeleton } from '../../shared/components/skeletons';
import { FileSizePipe, TimeAgoPipe } from '../../shared/pipes/format.pipes';
import { ActivityEvent } from '../../core/models/knowledge.models';

@Component({
  selector: 'dh-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, FileIcon, StatusPill, Avatar, RowSkeleton, FileSizePipe, TimeAgoPipe],
  host: { class: 'block' },
  template: `
    <!-- ── Hero ────────────────────────────────────────────────────────── -->
    <section class="dh-aurora border-b border-hairline px-6 pt-10 pb-8 lg:px-10">
      <div class="mx-auto max-w-6xl">
        <div class="flex flex-wrap items-end justify-between gap-6">
          <div class="min-w-0">
            <p class="dh-eyebrow mb-2">{{ today }}</p>
            <h1 class="text-[26px] leading-tight font-semibold text-ink">
              {{ greeting() }}, <span class="dh-gradient-text">Ana</span>
            </h1>
            <p class="mt-2 max-w-xl text-[13.5px] leading-relaxed text-muted">
              Your team's documentation, in one place. Upload it, organise it, and — from phase 3 —
              ask questions that are answered only from what's actually indexed.
            </p>

            <div class="mt-5 flex flex-wrap items-center gap-2">
              <a
                routerLink="/browse"
                [queryParams]="{ upload: 1 }"
                class="flex h-9 items-center gap-2 rounded-dh px-4 text-[13px] font-medium text-white transition hover:brightness-110"
                style="background: linear-gradient(135deg, var(--dh-brand-600), var(--dh-brand-500))"
              >
                <i class="pi pi-upload text-[12px]"></i>
                Upload documents
              </a>
              <a
                routerLink="/browse"
                class="flex h-9 items-center gap-2 rounded-dh border border-hairline px-4 text-[13px] font-medium text-ink transition hover:bg-surface-2"
              >
                <i class="pi pi-folder-open text-[12px]"></i>
                Browse library
              </a>
            </div>
          </div>

          <!-- The single hero figure for this view. -->
          <div class="shrink-0">
            <p class="dh-eyebrow mb-1">Searchable right now</p>
            <p class="text-[52px] leading-none font-semibold tracking-tight text-ink">
              {{ stats()?.indexed ?? '—' }}
            </p>
            <p class="mt-1.5 text-[12.5px] text-muted">
              of {{ stats()?.documents ?? '—' }} documents · {{ stats()?.chunks ?? 0 }} chunks
            </p>
          </div>
        </div>
      </div>
    </section>

    <div class="mx-auto max-w-6xl px-6 py-7 lg:px-10">
      <!-- ── KPI row ───────────────────────────────────────────────────── -->
      <div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
        @for (tile of tiles(); track tile.label) {
          <div class="dh-card dh-card-interactive p-4">
            <div class="flex items-start justify-between gap-2">
              <p class="text-[12px] text-muted">{{ tile.label }}</p>
              <span
                class="grid size-7 place-items-center rounded-[9px]"
                [style.color]="tile.tint"
                [style.background]="'color-mix(in oklab, ' + tile.tint + ' 14%, transparent)'"
              >
                <i class="pi text-[12px]" [class]="tile.icon"></i>
              </span>
            </div>
            <p class="mt-2 text-[28px] leading-none font-semibold text-ink">{{ tile.value }}</p>
            <p class="mt-1.5 text-[11.5px] text-subtle">{{ tile.hint }}</p>
          </div>
        }
      </div>

      <!-- ── Ingestion pipeline meter ──────────────────────────────────── -->
      <div class="dh-card mt-3 p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p class="text-[13px] font-semibold text-ink">Ingestion pipeline</p>
            <p class="mt-0.5 text-[11.5px] text-muted">
              Only indexed documents are visible to search and, later, to the assistant.
            </p>
          </div>
          <a
            routerLink="/browse"
            class="text-[12px] font-medium text-brand-400 transition hover:underline"
            >Open library →</a
          >
        </div>

        <!-- Stacked meter: 2px surface gaps do the separating, not borders. -->
        <div class="mt-4 flex h-2.5 gap-[2px] overflow-hidden rounded-full bg-surface-3">
          @for (seg of segments(); track seg.label) {
            @if (seg.percent > 0) {
              <div
                class="h-full first:rounded-l-full last:rounded-r-full transition-[width] duration-500"
                [style.width.%]="seg.percent"
                [style.background]="seg.tint"
                [attr.title]="seg.label + ': ' + seg.count"
              ></div>
            }
          }
        </div>

        <div class="mt-3 flex flex-wrap gap-x-6 gap-y-2">
          @for (seg of segments(); track seg.label) {
            <div class="flex items-center gap-2">
              <i class="pi text-[11px]" [class]="seg.icon" [style.color]="seg.tint"></i>
              <span class="text-[12px] text-muted">{{ seg.label }}</span>
              <span class="text-[12px] font-medium text-ink tabular-nums">{{ seg.count }}</span>
            </div>
          }
        </div>
      </div>

      <!-- ── Failed-ingestion callout ──────────────────────────────────── -->
      @if ((stats()?.failed ?? 0) > 0) {
        <div
          class="mt-3 flex flex-wrap items-center gap-3 rounded-dh-lg border p-4"
          style="border-color: color-mix(in oklab, var(--dh-status-failed) 30%, transparent);
                 background: color-mix(in oklab, var(--dh-status-failed) 8%, transparent)"
        >
          <i class="pi pi-exclamation-triangle text-[15px] text-status-failed"></i>
          <div class="min-w-0 flex-1">
            <p class="text-[13px] font-medium text-ink">
              {{ stats()?.failed }} documents failed to index
            </p>
            <p class="mt-0.5 text-[12px] text-muted">
              They are uploaded and browsable, but invisible to search — nobody will be told they
              exist.
            </p>
          </div>
          <button
            type="button"
            class="h-8 shrink-0 rounded-dh border border-hairline bg-surface-1 px-3 text-[12.5px] font-medium text-ink transition hover:bg-surface-2"
            (click)="store.showOnlyFailed()"
            routerLink="/browse"
          >
            Review them
          </button>
        </div>
      }

      <!-- ── Two-column: recent docs + activity ────────────────────────── -->
      <div class="mt-6 grid gap-4 lg:grid-cols-[1.55fr_1fr]">
        <section class="dh-card overflow-hidden">
          <header class="flex items-center justify-between border-b border-hairline px-4 py-3">
            <h2 class="text-[13px] font-semibold text-ink">Recently updated</h2>
            <a routerLink="/browse" class="text-[12px] text-brand-400 hover:underline">View all</a>
          </header>

          @if (store.loading()) {
            <dh-row-skeleton [count]="6" />
          } @else {
            @for (doc of recent(); track doc.id) {
              <a
                [routerLink]="['/docs', doc.id]"
                class="flex items-center gap-3 border-b border-hairline px-4 py-2.5 transition last:border-0 hover:bg-surface-2/60"
              >
                <dh-file-icon [kind]="doc.kind" size="sm" />
                <div class="min-w-0 flex-1">
                  <p class="truncate text-[13px] font-medium text-ink">{{ doc.title }}</p>
                  <p class="truncate text-[11.5px] text-subtle">
                    {{ doc.owner.name }} · {{ doc.updatedAt | timeAgo }} ·
                    {{ doc.sizeBytes | fileSize }}
                  </p>
                </div>
                <dh-status-pill [status]="doc.status" [progress]="doc.indexProgress" />
              </a>
            }
          }
        </section>

        <section class="dh-card overflow-hidden">
          <header class="border-b border-hairline px-4 py-3">
            <h2 class="text-[13px] font-semibold text-ink">Activity</h2>
          </header>
          <div class="px-4 py-3">
            @if (activity(); as events) {
              <ol class="relative space-y-3.5">
                @for (event of events; track event.id) {
                  <li class="flex gap-3">
                    <dh-avatar [person]="event.actor" size="sm" />
                    <div class="min-w-0 flex-1">
                      <p class="text-[12.5px] leading-snug text-muted">
                        <span class="font-medium text-ink">{{ event.actor.name }}</span>
                        {{ verb(event) }}
                        @if (event.targetId) {
                          <a
                            [routerLink]="['/docs', event.targetId]"
                            class="font-medium text-ink hover:underline"
                            >{{ event.target }}</a
                          >
                        } @else {
                          <span class="font-medium text-ink">{{ event.target }}</span>
                        }
                      </p>
                      <p class="mt-0.5 text-[11px] text-subtle">{{ event.at | timeAgo }}</p>
                    </div>
                  </li>
                }
              </ol>
            } @else {
              @for (i of [1, 2, 3, 4, 5]; track i) {
                <div class="dh-skeleton mb-3 h-9 rounded"></div>
              }
            }
          </div>
        </section>
      </div>
    </div>
  `,
})
export class Dashboard {
  protected readonly store = inject(LibraryStore);
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
        value: this.compactBytes(s?.storageBytes ?? 0),
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

  protected verb(event: ActivityEvent): string {
    switch (event.type) {
      case 'uploaded':
        return 'uploaded';
      case 'indexed':
        return 'finished indexing';
      case 'failed':
        return 'hit an ingestion failure on';
      case 'folder-created':
        return 'created the folder';
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
