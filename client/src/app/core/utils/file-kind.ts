import { FileKind } from '../models/knowledge.models';

interface KindPresentation {
  /** PrimeIcons class. */
  icon: string;
  label: string;
  /** Tint used for the icon chip background/foreground. */
  tint: string;
}

const PRESENTATION: Record<FileKind, KindPresentation> = {
  pdf: { icon: 'pi-file-pdf', label: 'PDF', tint: '#f43f5e' },
  word: { icon: 'pi-file-word', label: 'Word', tint: '#3b82f6' },
  slides: { icon: 'pi-desktop', label: 'Slides', tint: '#f97316' },
  sheet: { icon: 'pi-file-excel', label: 'Spreadsheet', tint: '#10b981' },
  markdown: { icon: 'pi-file-edit', label: 'Markdown', tint: '#8b5cf6' },
  text: { icon: 'pi-file', label: 'Text', tint: '#64748b' },
  code: { icon: 'pi-code', label: 'Code', tint: '#22d3ee' },
  sql: { icon: 'pi-database', label: 'SQL', tint: '#0ea5e9' },
  image: { icon: 'pi-image', label: 'Image', tint: '#ec4899' },
  diagram: { icon: 'pi-sitemap', label: 'Diagram', tint: '#a855f7' },
  archive: { icon: 'pi-box', label: 'Archive', tint: '#eab308' },
  unknown: { icon: 'pi-file', label: 'File', tint: '#64748b' },
};

const EXTENSION_MAP: Record<string, FileKind> = {
  pdf: 'pdf',
  doc: 'word',
  docx: 'word',
  ppt: 'slides',
  pptx: 'slides',
  xls: 'sheet',
  xlsx: 'sheet',
  csv: 'sheet',
  md: 'markdown',
  mdx: 'markdown',
  txt: 'text',
  log: 'text',
  json: 'code',
  yml: 'code',
  yaml: 'code',
  ts: 'code',
  cs: 'code',
  sql: 'sql',
  png: 'image',
  jpg: 'image',
  jpeg: 'image',
  svg: 'image',
  gif: 'image',
  drawio: 'diagram',
  mmd: 'diagram',
  vsdx: 'diagram',
  zip: 'archive',
};

export function kindFromExtension(extension: string): FileKind {
  return EXTENSION_MAP[extension.replace('.', '').toLowerCase()] ?? 'unknown';
}

/**
 * Every extension that maps to a kind. The UI filters by kind ("Slides") while
 * the API filters by extension, so the gateway expands one into the other.
 */
export function extensionsForKind(kind: FileKind): string[] {
  return Object.entries(EXTENSION_MAP)
    .filter(([, mapped]) => mapped === kind)
    .map(([extension]) => extension);
}

export function kindFromFileName(fileName: string): FileKind {
  const ext = fileName.split('.').pop() ?? '';
  return kindFromExtension(ext);
}

export function presentationFor(kind: FileKind): KindPresentation {
  return PRESENTATION[kind] ?? PRESENTATION.unknown;
}

export function formatBytes(bytes: number): string {
  if (bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const exp = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  const value = bytes / Math.pow(1024, exp);
  return `${value >= 10 || exp === 0 ? Math.round(value) : value.toFixed(1)} ${units[exp]}`;
}

export function relativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime();
  const mins = Math.round(diffMs / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  if (days < 30) return `${days}d ago`;
  const months = Math.round(days / 30);
  if (months < 12) return `${months}mo ago`;
  return `${Math.round(months / 12)}y ago`;
}
