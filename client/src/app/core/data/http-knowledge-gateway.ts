import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import {
  Account,
  ActivityEvent,
  AskRequest,
  AuthOptions,
  NewAccount,
  ChatEvent,
  ChatSession,
  ChatTranscript,
  DocumentDetail,
  DocumentQuery,
  DocumentSummary,
  FileKind,
  Folder,
  IngestionStatus,
  KnowledgeSource,
  LibraryStats,
  MatchStrategy,
  Person,
  RepositoryProbe,
  RepositorySource,
  SearchQuery,
  SearchResponse,
  SignedInUser,
  UserRole,
} from '../models/knowledge.models';
import { extensionsForKind, kindFromExtension } from '../utils/file-kind';
import { KnowledgeGateway } from './knowledge-gateway';

// ---- wire formats -----------------------------------------------------------
// Mirrors the ViewModels the API returns. Kept private to this file so the rest
// of the app only ever sees the client's own models.

interface ApiUser {
  id: string;
  name: string;
  email: string;
  initials: string;
}

interface ApiFolder {
  id: string;
  parentId: string | null;
  name: string;
  path: string;
  documentCount: number;
}

interface ApiDocument {
  id: string;
  folderId: string;
  title: string;
  description: string | null;
  fileName: string;
  extension: string;
  sizeBytes: number;
  version: number;
  tags: string[];
  owner: ApiUser;
  status: IngestionStatus;
  failureReason: string | null;
  chunkCount: number | null;
  isStarred: boolean;
  createdAt: string;
  updatedAt: string;
}

interface ApiDocumentDetail {
  document: ApiDocument;
  breadcrumb: ApiFolder[];
  versions: {
    version: number;
    sizeBytes: number;
    note: string | null;
    changedBy: ApiUser;
    changedAt: string;
  }[];
  sections: { chunkId: number; heading: string; body: string; tokenCount: number }[];
}

interface ApiSearchResult {
  documentId: string;
  title: string;
  fileName: string;
  extension: string;
  folderId: string;
  folderPath: string;
  chunkId: number;
  heading: string;
  snippet: string;
  score: number;
  matchedBy: MatchStrategy;
}

interface ApiSearchResponse {
  query: string;
  totalMatches: number;
  elapsedMs: number;
  terms: string[];
  results: ApiSearchResult[];
  diagnostics: {
    keywordMatches: number;
    vectorMatches: number;
    embeddingProvider: string;
    vectorSearchAvailable: boolean;
    vectorSearchError: string | null;
  };
}

interface ApiStats {
  documents: number;
  indexed: number;
  inPipeline: number;
  failed: number;
  folders: number;
  storageBytes: number;
  chunks: number;
}

/**
 * Talks to the ASP.NET Core API. The only implementation detail that leaks out
 * of this file is the shape of the wire format — everything above it keeps
 * working against `KnowledgeGateway`.
 *
 * In development the Angular dev server proxies `/api` to localhost:5080
 * (see proxy.conf.json), so requests are same-origin and CORS never applies.
 */
@Injectable()
export class HttpKnowledgeGateway extends KnowledgeGateway {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  // ---- reads ---------------------------------------------------------------

  folders(): Observable<Folder[]> {
    return this.http
      .get<ApiFolder[]>(`${this.base}/folders`)
      .pipe(map((folders) => folders.map(toFolder)));
  }

  documents(query: DocumentQuery): Observable<DocumentSummary[]> {
    return this.http
      .get<ApiDocument[]>(`${this.base}/documents`, { params: toQueryParams(query) })
      .pipe(map((documents) => documents.map(toSummary)));
  }

  document(id: string): Observable<DocumentDetail | undefined> {
    return this.http.get<ApiDocumentDetail>(`${this.base}/documents/${id}`).pipe(
      map((detail) => ({
        ...toSummary(detail.document),
        breadcrumb: detail.breadcrumb.map(toFolder),
        versions: detail.versions.map((version, index) => ({
          version: version.version,
          changedBy: toPerson(version.changedBy),
          changedAt: version.changedAt,
          note: version.note ?? '',
          sizeBytes: version.sizeBytes,
          current: index === 0,
        })),
        // Empty until ingestion finishes; the detail screen shows pipeline
        // state instead of a preview while that is the case.
        sections: detail.sections,
        citedInAnswers: 0,
        createdAt: detail.document.createdAt,
      })),
    );
  }

  stats(): Observable<LibraryStats> {
    return this.http.get<ApiStats>(`${this.base}/documents/stats`).pipe(
      map((stats) => ({
        documents: stats.documents,
        indexed: stats.indexed,
        indexing: stats.inPipeline,
        failed: stats.failed,
        folders: stats.folders,
        storageBytes: stats.storageBytes,
        chunks: stats.chunks,
      })),
    );
  }

  /**
   * No audit trail exists server-side yet — the blueprint's AuditLog table is
   * phase 5 work. Returning an empty list keeps the dashboard honest: it shows
   * its "no activity yet" state rather than inventing events.
   */
  activity(): Observable<ActivityEvent[]> {
    return of([]);
  }

  people(): Observable<Person[]> {
    return this.http
      .get<ApiUser[]>(`${this.base}/documents/owners`)
      .pipe(map((owners) => owners.map(toPerson)));
  }

  allTags(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/documents/tags`);
  }

  // ---- authentication ------------------------------------------------------

  currentUser(): Observable<SignedInUser | null> {
    return this.http.get<SignedInUser>(`${this.base}/auth/me`).pipe(
      // 401 is the answer, not a failure: it is what the server says when
      // nobody is signed in, which is the ordinary state on first load.
      catchError(() => of(null)),
    );
  }

  authOptions(): Observable<AuthOptions> {
    return this.http.get<AuthOptions>(`${this.base}/auth/options`);
  }

  signIn(email: string, password: string): Observable<SignedInUser> {
    return this.http.post<SignedInUser>(`${this.base}/auth/login`, { email, password });
  }

  signOut(): Observable<void> {
    return this.http.post<void>(`${this.base}/auth/logout`, {});
  }

  accounts(): Observable<Account[]> {
    return this.http.get<Account[]>(`${this.base}/users`);
  }

  createAccount(input: NewAccount): Observable<Account> {
    return this.http.post<Account>(`${this.base}/users`, input);
  }

  changeAccountRole(id: string, role: UserRole): Observable<Account> {
    return this.http.put<Account>(`${this.base}/users/${id}/role`, { role });
  }

  setAccountEnabled(id: string, enabled: boolean): Observable<Account> {
    return this.http.post<Account>(
      `${this.base}/users/${id}/${enabled ? 'enable' : 'disable'}`,
      {},
    );
  }

  knowledgeSources(): Observable<KnowledgeSource[]> {
    // Passed through as-is: the API already returns the three states and an
    // actionable detail line, and a client that reinterpreted them would be a
    // second place for "what does inactive mean" to drift.
    return this.http.get<KnowledgeSource[]>(`${this.base}/sources`);
  }

  repositorySource(): Observable<RepositorySource> {
    return this.http.get<RepositorySource>(`${this.base}/sources/repository`);
  }

  saveRepositorySource(endpoint: string | null, isEnabled: boolean): Observable<RepositorySource> {
    return this.http.put<RepositorySource>(`${this.base}/sources/repository`, {
      endpoint,
      isEnabled,
    });
  }

  resetRepositorySource(): Observable<RepositorySource> {
    return this.http.delete<RepositorySource>(`${this.base}/sources/repository`);
  }

  testRepositorySource(endpoint: string | null): Observable<RepositoryProbe> {
    return this.http.post<RepositoryProbe>(`${this.base}/sources/repository/test`, { endpoint });
  }

  search(query: SearchQuery): Observable<SearchResponse> {
    let params = new HttpParams().set('query', query.text.trim());

    if (query.folderId) params = params.set('folderId', query.folderId);
    if (query.ownerId) params = params.set('ownerId', query.ownerId);
    for (const tag of query.tags ?? []) params = params.append('tag', tag);

    // Same kind-to-extension expansion the library filter uses.
    for (const kind of query.kinds ?? []) {
      for (const extension of extensionsForKind(kind)) {
        params = params.append('extension', extension);
      }
    }

    return this.http.get<ApiSearchResponse>(`${this.base}/search`, { params }).pipe(
      map((response) => ({
        query: response.query,
        totalMatches: response.totalMatches,
        elapsedMs: response.elapsedMs,
        terms: response.terms,
        results: response.results.map((result) => ({
          ...result,
          kind: kindFromExtension(result.extension) as FileKind,
        })),
        diagnostics: {
          ...response.diagnostics,
          vectorSearchError: response.diagnostics.vectorSearchError ?? undefined,
        },
      })),
    );
  }

  // ---- folder commands -----------------------------------------------------

  createFolder(parentId: string | null, name: string): Observable<Folder> {
    return this.http
      .post<ApiFolder>(`${this.base}/folders`, { parentId, name })
      .pipe(map(toFolder));
  }

  renameFolder(id: string, name: string): Observable<void> {
    return this.http.put<ApiFolder>(`${this.base}/folders/${id}`, { name }).pipe(map(() => void 0));
  }

  deleteFolder(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/folders/${id}`);
  }

  // ---- document commands ---------------------------------------------------

  uploadFiles(folderId: string, files: File[]): Observable<void> {
    if (!files.length) return of(void 0);

    // Uploaded one at a time rather than as a single batch, so one rejected
    // file (too large, blocked type) cannot fail the others.
    return forkJoin(
      files.map((file) => {
        const form = new FormData();
        form.append('file', file, file.name);
        return this.http.post<ApiDocument>(`${this.base}/documents`, form, {
          params: new HttpParams().set('folderId', folderId),
        });
      }),
    ).pipe(map(() => void 0));
  }

  // ---- assistant -----------------------------------------------------------

  /**
   * Streams an answer over server-sent events.
   *
   * Uses `fetch` rather than HttpClient: HttpClient buffers the whole response
   * before emitting, which would defeat streaming entirely. Aborting the fetch
   * on unsubscribe also cancels the request server-side, so navigating away
   * stops the model rather than leaving it generating into nothing.
   */
  ask(request: AskRequest): Observable<ChatEvent> {
    return new Observable<ChatEvent>((subscriber) => {
      const controller = new AbortController();

      void (async () => {
        try {
          const response = await fetch(`${this.base}/chat`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request),
            signal: controller.signal,
            // The interceptor cannot reach this call — it is fetch, not
            // HttpClient — so the session cookie is attached here.
            credentials: 'include',
          });

          if (!response.ok || !response.body) {
            // Errors before the stream starts come back as problem details.
            const problem = await response.json().catch(() => null);
            throw new Error(
              problem?.detail ?? problem?.title ?? `Request failed (${response.status})`,
            );
          }

          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';

          for (;;) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            // Events are separated by a blank line; a partial frame at the end
            // of a chunk stays in the buffer until the rest arrives.
            const frames = buffer.split('\n\n');
            buffer = frames.pop() ?? '';

            for (const frame of frames) {
              const parsed = parseSseFrame(frame);
              if (parsed) subscriber.next(parsed);
            }
          }

          subscriber.complete();
        } catch (error) {
          // An abort is an unsubscribe, not a failure — the subscriber is
          // already gone and emitting an error would be noise.
          if (controller.signal.aborted) return;
          subscriber.error(error);
        }
      })();

      return () => controller.abort();
    });
  }

  chatSessions(): Observable<ChatSession[]> {
    return this.http.get<ChatSession[]>(`${this.base}/chat/sessions`);
  }

  chatTranscript(sessionId: string): Observable<ChatTranscript> {
    return this.http.get<ChatTranscript>(`${this.base}/chat/sessions/${sessionId}`);
  }

  deleteChatSession(sessionId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/chat/sessions/${sessionId}`);
  }

  retryIngestion(documentId: string): Observable<void> {
    return this.http
      .post<ApiDocument>(`${this.base}/documents/${documentId}/reindex`, {})
      .pipe(map(() => void 0));
  }

  toggleStar(documentId: string): Observable<void> {
    // The API takes an explicit value, so read the current one first rather
    // than assuming what the UI last rendered.
    return this.http.get<ApiDocumentDetail>(`${this.base}/documents/${documentId}`).pipe(
      switchMap((detail) =>
        this.http.patch<ApiDocument>(`${this.base}/documents/${documentId}`, {
          isStarred: !detail.document.isStarred,
        }),
      ),
      map(() => void 0),
    );
  }

  moveDocument(documentId: string, folderId: string): Observable<void> {
    return this.http
      .post<ApiDocument>(`${this.base}/documents/${documentId}/move`, { folderId })
      .pipe(map(() => void 0));
  }

  deleteDocument(documentId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/documents/${documentId}`);
  }
}

// ---- mapping ----------------------------------------------------------------

function toFolder(folder: ApiFolder): Folder {
  return {
    id: folder.id,
    parentId: folder.parentId,
    name: folder.name,
    path: folder.path,
    documentCount: folder.documentCount,
  };
}

function toPerson(user: ApiUser): Person {
  return {
    id: user.id,
    name: user.name,
    initials: user.initials,
    // Derived rather than stored: the same person always gets the same colour
    // without the server needing to know anything about presentation.
    tint: tintFor(user.id),
  };
}

function toSummary(document: ApiDocument): DocumentSummary {
  return {
    id: document.id,
    folderId: document.folderId,
    title: document.title,
    fileName: document.fileName,
    kind: kindFromExtension(document.extension) as FileKind,
    extension: document.extension,
    sizeBytes: document.sizeBytes,
    version: document.version,
    tags: document.tags,
    owner: toPerson(document.owner),
    updatedAt: document.updatedAt,
    status: document.status,
    chunkCount: document.chunkCount ?? undefined,
    failureReason: document.failureReason ?? undefined,
    description: document.description ?? undefined,
    starred: document.isStarred,
  };
}

function toQueryParams(query: DocumentQuery): HttpParams {
  let params = new HttpParams();

  if (query.folderId) params = params.set('folderId', query.folderId);
  if (query.recursive !== undefined) params = params.set('recursive', query.recursive);
  if (query.text?.trim()) params = params.set('text', query.text.trim());
  if (query.ownerId) params = params.set('ownerId', query.ownerId);
  if (query.starredOnly) params = params.set('starredOnly', true);
  if (query.sort) params = params.set('sort', query.sort);

  for (const status of query.statuses ?? []) params = params.append('status', status);
  for (const tag of query.tags ?? []) params = params.append('tag', tag);

  // The UI filters by file *kind* ("Slides"); the API filters by extension,
  // so one kind expands to every extension that maps to it.
  for (const kind of query.kinds ?? []) {
    for (const extension of extensionsForKind(kind)) {
      params = params.append('extension', extension);
    }
  }

  return params;
}

/**
 * Turns one `event:`/`data:` frame into a typed event.
 *
 * The event name carries the discriminator; the payload is merged onto it so
 * the result matches the `ChatEvent` union. Unknown names are ignored rather
 * than thrown on — a future server event should not break an older client.
 */
function parseSseFrame(frame: string): ChatEvent | null {
  let name = '';
  let data = '';

  for (const line of frame.split('\n')) {
    if (line.startsWith('event: ')) name = line.slice(7).trim();
    else if (line.startsWith('data: ')) data += line.slice(6);
  }

  if (!name || !data) return null;
  if (!['session', 'sources', 'token', 'done', 'error'].includes(name)) return null;

  try {
    return { type: name, ...JSON.parse(data) } as ChatEvent;
  } catch {
    return null;
  }
}

/** Deterministic avatar colour from an id — stable across sessions and users. */
const TINTS = ['#7c5cff', '#22d3ee', '#f472b6', '#10b981', '#f97316', '#eab308', '#a855f7'];

function tintFor(id: string): string {
  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
  return TINTS[hash % TINTS.length];
}
