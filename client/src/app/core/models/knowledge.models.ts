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
  /**
   * Where in the document this came from — "Page 4", "Slide 2", a Markdown
   * heading, a worksheet name. Which one depends on the file type, so it is
   * shown as-is rather than parsed.
   */
  heading: string;
  body: string;
  tokenCount: number;
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

// ---- search -----------------------------------------------------------------

/** Which retrieval branch produced a result. */
export type MatchStrategy = 'keyword' | 'vector' | 'both';

export interface SearchResult {
  documentId: string;
  title: string;
  fileName: string;
  kind: FileKind;
  extension: string;
  folderId: string;
  folderPath: string;
  /** Chunk position — links straight to the passage: `/docs/:id?chunk=:chunkId`. */
  chunkId: number;
  heading: string;
  snippet: string;
  score: number;
  matchedBy: MatchStrategy;
}

/**
 * How the two branches contributed. Surfaced in the UI because hybrid results
 * are otherwise unexplainable — this is what separates "nothing matched" from
 * "semantic matching is down".
 */
export interface SearchDiagnostics {
  keywordMatches: number;
  vectorMatches: number;
  embeddingProvider: string;
  vectorSearchAvailable: boolean;
  vectorSearchError?: string;
}

export interface SearchResponse {
  query: string;
  totalMatches: number;
  elapsedMs: number;
  /** Normalised query words, so the client highlights exactly what was searched. */
  terms: string[];
  results: SearchResult[];
  diagnostics: SearchDiagnostics;
}

/** Filters carried alongside a search, mirroring the library's own. */
export interface SearchQuery {
  text: string;
  folderId?: string | null;
  kinds?: FileKind[];
  tags?: string[];
  ownerId?: string;
}

// ---- assistant --------------------------------------------------------------

/** A source backing an answer, resolvable to the exact passage. */
export interface Citation {
  /** The bracketed number used in the answer text. */
  marker: number;
  documentId: string;
  documentTitle: string;
  /** Chunk position — links to `/docs/:documentId?chunk=:chunkId`. */
  chunkId: number;
  heading: string;
}

export interface ChatSession {
  id: string;
  title: string;
  messageCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  citations: Citation[];
  /**
   * True when the assistant declined for lack of grounding. Rendered as its
   * own state — an honest "I don't know" is the designed outcome for an
   * unanswerable question, not an error.
   */
  isRefusal: boolean;
  createdAt: string;
}

export interface ChatTranscript {
  session: ChatSession;
  messages: ChatMessage[];
}

/**
 * One server-sent event from an in-flight answer. Discriminated on `type` so
 * the component can render sources before the first token arrives.
 */
export type ChatEvent =
  | { type: 'session'; sessionId: string; title: string }
  | { type: 'sources'; sources: Citation[] }
  | { type: 'token'; text: string }
  | { type: 'done'; messageId: string; citations: Citation[]; isRefusal: boolean }
  | { type: 'error'; reason: string };

export interface AskRequest {
  question: string;
  /** Omitted to start a new conversation. */
  sessionId?: string | null;
  folderId?: string | null;
}

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

/**
 * Whether a knowledge source is contributing to answers — and if not, why.
 *
 * Three states rather than a boolean because they mean genuinely different
 * things: `inactive` is a source switched off by design, `unavailable` is one
 * that should be working and is not. Rendering them alike would train people
 * to ignore the second.
 */
export type KnowledgeSourceState = 'active' | 'inactive' | 'unavailable';

/** One body of knowledge the assistant may ground an answer in. */
export interface KnowledgeSource {
  name: string;
  displayName: string;
  description: string;
  state: KnowledgeSourceState;
  /** Why it is in that state, in one actionable sentence. */
  detail: string;
}
