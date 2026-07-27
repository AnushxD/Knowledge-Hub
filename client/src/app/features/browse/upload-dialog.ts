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
  templateUrl: './upload-dialog.html',
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
