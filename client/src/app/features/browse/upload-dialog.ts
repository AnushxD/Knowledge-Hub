import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { LibraryStore } from '../../core/state/library-store';
import { FileIcon } from '../../shared/components/file-icon';
import { kindFromFileName } from '../../core/utils/file-kind';
import { FileSizePipe } from '../../shared/pipes/format.pipes';

interface Staged {
  file: File;
  progress: number;
  error?: string;
}

const MAX_BYTES = 25 * 1024 * 1024;
const BLOCKED = ['exe', 'dll', 'bat', 'sh', 'msi'];

@Component({
  selector: 'dh-upload-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FileIcon, FileSizePipe],
  template: `
    <div
      class="fixed inset-0 z-50 flex items-center justify-center px-4"
      role="dialog"
      aria-modal="true"
      aria-label="Upload documents"
    >
      <div class="absolute inset-0 bg-black/45 backdrop-blur-[2px]" (click)="close.emit()"></div>

      <div
        class="dh-rise relative flex w-full max-w-lg flex-col overflow-hidden rounded-dh-xl border border-hairline bg-surface-1 shadow-dh-lg"
      >
        <header class="flex items-center gap-3 border-b border-hairline px-5 py-3.5">
          <div class="min-w-0 flex-1">
            <h2 class="text-[14px] font-semibold text-ink">Upload documents</h2>
            <p class="mt-0.5 truncate text-[12px] text-muted">
              Into <span class="text-ink">{{ targetName() }}</span>
            </p>
          </div>
          <button
            type="button"
            class="grid size-8 place-items-center rounded-lg text-subtle transition hover:bg-surface-2 hover:text-ink"
            (click)="close.emit()"
            aria-label="Close"
          >
            <i class="pi pi-times text-[13px]"></i>
          </button>
        </header>

        <div class="px-5 py-4">
          <label
            class="flex cursor-pointer flex-col items-center justify-center rounded-dh-lg border-2 border-dashed px-6 py-9 text-center transition"
            [class]="
              dragging()
                ? 'border-brand-400 bg-brand-500/8'
                : 'border-hairline hover:border-hairline-strong hover:bg-surface-2/50'
            "
            (dragover)="onDragOver($event)"
            (dragleave)="dragging.set(false)"
            (drop)="onDrop($event)"
          >
            <input type="file" class="hidden" multiple (change)="onPick($event)" />
            <span
              class="dh-ai-surface mb-3 grid size-12 place-items-center rounded-[16px] text-[18px] text-brand-400"
            >
              <i class="pi pi-cloud-upload"></i>
            </span>
            <span class="text-[13px] font-medium text-ink">
              Drop files here, or <span class="text-brand-400">browse</span>
            </span>
            <span class="mt-1 text-[11.5px] text-subtle">
              PDF, Word, PowerPoint, Excel, Markdown, images — up to 25 MB each
            </span>
          </label>

          @if (staged().length) {
            <ul class="mt-4 max-h-56 space-y-1.5 overflow-y-auto">
              @for (item of staged(); track item.file.name) {
                <li class="flex items-center gap-2.5 rounded-dh border border-hairline px-3 py-2">
                  <dh-file-icon [kind]="kindOf(item.file.name)" size="sm" />
                  <div class="min-w-0 flex-1">
                    <p class="truncate text-[12.5px] text-ink">{{ item.file.name }}</p>
                    @if (item.error) {
                      <p class="text-[11px] text-status-failed">{{ item.error }}</p>
                    } @else {
                      <p class="text-[11px] text-subtle">{{ item.file.size | fileSize }}</p>
                    }
                  </div>
                  <button
                    type="button"
                    class="grid size-6 place-items-center rounded-md text-subtle transition hover:bg-surface-2 hover:text-ink"
                    (click)="remove(item)"
                    [attr.aria-label]="'Remove ' + item.file.name"
                  >
                    <i class="pi pi-times text-[10px]"></i>
                  </button>
                </li>
              }
            </ul>
          }

          <!-- Set expectations before the upload, not after it. -->
          <div class="dh-ai-surface mt-4 flex gap-2.5 rounded-dh p-3">
            <i class="pi pi-info-circle mt-px text-[12px] text-brand-400"></i>
            <p class="text-[11.5px] leading-relaxed text-muted">
              Uploaded files are queued for ingestion — extracted, chunked and embedded in the
              background. They become searchable once their status turns
              <span class="font-medium text-ink">Indexed</span>.
            </p>
          </div>
        </div>

        <footer class="flex items-center gap-2 border-t border-hairline px-5 py-3">
          <span class="flex-1 text-[12px] text-subtle">
            @if (validCount()) {
              {{ validCount() }} file{{ validCount() === 1 ? '' : 's' }} ready
            }
          </span>
          <button
            type="button"
            class="h-9 rounded-dh border border-hairline px-3.5 text-[13px] font-medium text-ink transition hover:bg-surface-2"
            (click)="close.emit()"
          >
            Cancel
          </button>
          <button
            type="button"
            class="h-9 rounded-dh px-4 text-[13px] font-medium text-white transition enabled:hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-45"
            style="background: linear-gradient(135deg, var(--dh-brand-600), var(--dh-brand-500))"
            [disabled]="!validCount()"
            (click)="submit()"
          >
            Upload {{ validCount() || '' }}
          </button>
        </footer>
      </div>
    </div>
  `,
})
export class UploadDialog {
  private readonly store = inject(LibraryStore);

  readonly folderId = input<string | null>(null);
  /** Files dropped onto the page before the dialog opened. */
  readonly seedFiles = input<File[]>([]);
  readonly close = output<void>();

  protected readonly staged = signal<Staged[]>([]);
  protected readonly dragging = signal(false);

  constructor() {
    effect(() => {
      const seed = this.seedFiles();
      if (seed.length) this.add(seed);
    });
  }

  protected readonly targetName = computed(
    () => this.store.folders()?.find((f) => f.id === this.folderId())?.path ?? 'All documents',
  );

  protected readonly validCount = computed(() => this.staged().filter((s) => !s.error).length);

  protected kindOf(name: string) {
    return kindFromFileName(name);
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.add(Array.from(event.dataTransfer?.files ?? []));
  }

  protected onPick(event: Event): void {
    this.add(Array.from((event.target as HTMLInputElement).files ?? []));
  }

  private add(files: File[]): void {
    const staged = files.map<Staged>((file) => ({
      file,
      progress: 0,
      error: this.validate(file),
    }));
    this.staged.update((current) => [
      ...current,
      ...staged.filter((s) => !current.some((c) => c.file.name === s.file.name)),
    ]);
  }

  /** Client-side guardrails; the API re-validates (blueprint §8). */
  private validate(file: File): string | undefined {
    const ext = (file.name.split('.').pop() ?? '').toLowerCase();
    if (BLOCKED.includes(ext)) return `.${ext} files are not allowed`;
    if (file.size > MAX_BYTES) return 'Larger than the 25 MB limit';
    return undefined;
  }

  protected remove(item: Staged): void {
    this.staged.update((s) => s.filter((x) => x !== item));
  }

  protected submit(): void {
    const files = this.staged()
      .filter((s) => !s.error)
      .map((s) => s.file);
    if (!files.length) return;
    this.store.upload(this.folderId() ?? 'f-eng', files);
    this.close.emit();
  }
}
