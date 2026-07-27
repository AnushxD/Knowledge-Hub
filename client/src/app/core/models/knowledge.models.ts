/**
 * Client-side view models. These mirror the API ViewModels described in the
 * architecture blueprint (§7) — the Angular app only ever sees ViewModels,
 * never DTOs or entities.
 */

/** Lifecycle of a document through the ingestion pipeline (blueprint §4). */
export type IngestionStatus = 'pending' | 'indexing' | 'indexed' | 'failed';

export type FileKind =
  | 'pdf'
  | 'word'
  | 'slides'
  | 'sheet'
  | 'markdown'
  | 'text'
  | 'code'
  | 'sql'
  | 'image'
  | 'diagram'
  | 'archive'
  | 'unknown';

export interface Folder {
  id: string;
  parentId: string | null;
  name: string;
  /** Materialised path, e.g. "Engineering/Onboarding". */
  path: string;
  documentCount: number;
  color?: string;
}

export interface DocumentSummary {
  id: string;
  folderId: string;
  title: string;
  fileName: string;
  kind: FileKind;
  extension: string;
  sizeBytes: number;
  version: number;
  tags: string[];
  owner: Person;
  updatedAt: string;
  status: IngestionStatus;
  /** 0–100, only meaningful while `status === 'indexing'`. */
  indexProgress?: number;
  /** Populated once indexed — how many chunks the doc contributed. */
  chunkCount?: number;
  /** Failure reason surfaced on the row when `status === 'failed'`. */
  failureReason?: string;
  description?: string;
  starred?: boolean;
}

export interface DocumentDetail extends DocumentSummary {
  breadcrumb: Folder[];
  versions: DocumentVersion[];
  /** Extracted text sections, used for preview + citation highlighting. */
  sections: DocumentSection[];
  citedInAnswers: number;
  createdAt: string;
}

export interface DocumentSection {
  /** Matches the chunk id used by citations (`/docs/:id?chunk=17`). */
  chunkId: number;
  heading: string;
  page: number;
  body: string;
}

export interface DocumentVersion {
  version: number;
  changedBy: Person;
  changedAt: string;
  note: string;
  sizeBytes: number;
  current: boolean;
}

export interface Person {
  id: string;
  name: string;
  initials: string;
  /** Deterministic avatar tint so the same person is the same colour everywhere. */
  tint: string;
}

export interface ActivityEvent {
  id: string;
  type: 'uploaded' | 'indexed' | 'failed' | 'updated' | 'folder-created';
  actor: Person;
  target: string;
  targetId?: string;
  at: string;
}

export interface LibraryStats {
  documents: number;
  indexed: number;
  indexing: number;
  failed: number;
  folders: number;
  storageBytes: number;
  chunks: number;
}

/** Filter state for the browser screen. */
export interface DocumentQuery {
  folderId?: string | null;
  /** Include documents in descendant folders. */
  recursive?: boolean;
  text?: string;
  kinds?: FileKind[];
  statuses?: IngestionStatus[];
  tags?: string[];
  ownerId?: string;
  starredOnly?: boolean;
  sort?: SortKey;
}

export type SortKey = 'updated-desc' | 'updated-asc' | 'name-asc' | 'name-desc' | 'size-desc';

export interface UploadTask {
  id: string;
  fileName: string;
  sizeBytes: number;
  kind: FileKind;
  folderId: string;
  progress: number;
  phase: 'uploading' | 'queued' | 'extracting' | 'embedding' | 'done' | 'error';
  error?: string;
}
