import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, map, of, switchMap } from 'rxjs';
import {
  ActivityEvent,
  DocumentDetail,
  DocumentQuery,
  DocumentSummary,
  FileKind,
  Folder,
  IngestionStatus,
  LibraryStats,
  Person,
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
  sections: { chunkId: number; heading: string; page: number; body: string }[];
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
        // Empty until the phase 2 ingestion pipeline produces chunks.
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

  /**
   * Re-ingestion needs the phase 2 pipeline to exist. Nothing can reach the
   * Failed state until then, so this path is currently unreachable rather than
   * silently doing nothing on a live document.
   */
  retryIngestion(): Observable<void> {
    return of(void 0);
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

/** Deterministic avatar colour from an id — stable across sessions and users. */
const TINTS = ['#7c5cff', '#22d3ee', '#f472b6', '#10b981', '#f97316', '#eab308', '#a855f7'];

function tintFor(id: string): string {
  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
  return TINTS[hash % TINTS.length];
}
