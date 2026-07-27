import { Pipe, PipeTransform } from '@angular/core';
import { formatBytes, relativeTime } from '../../core/utils/file-kind';

@Pipe({ name: 'fileSize' })
export class FileSizePipe implements PipeTransform {
  transform(bytes: number | undefined | null): string {
    return formatBytes(bytes ?? 0);
  }
}

@Pipe({ name: 'timeAgo' })
export class TimeAgoPipe implements PipeTransform {
  transform(iso: string | undefined | null): string {
    return iso ? relativeTime(iso) : '—';
  }
}
