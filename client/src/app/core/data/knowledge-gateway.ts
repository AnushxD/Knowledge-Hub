import { Observable } from 'rxjs';
import {
  ActivityEvent,
  DocumentDetail,
  DocumentQuery,
  DocumentSummary,
  Folder,
  LibraryStats,
  Person,
  SearchQuery,
  SearchResponse,
} from '../models/knowledge.models';

/**
 * The single seam between the UI and the backend.
 *
 * `HttpKnowledgeGateway` talks to the ASP.NET Core API and is the default;
 * `MockKnowledgeGateway` implements the same contract with in-memory data, for
 * working on screens without running the backend. Swapping them is one line in
 * `app.config.ts`. No component imports HttpClient directly — same "always
 * behind an interface" discipline the backend uses for its Data Access and
 * Integrations layers.
 */
export abstract class KnowledgeGateway {
  abstract folders(): Observable<Folder[]>;
  abstract documents(query: DocumentQuery): Observable<DocumentSummary[]>;
  abstract document(id: string): Observable<DocumentDetail | undefined>;
  abstract stats(): Observable<LibraryStats>;

  /**
   * Hybrid keyword + semantic search over indexed chunks. Distinct from
   * `documents()`, which filters the library by metadata — this searches
   * inside the content.
   */
  abstract search(query: SearchQuery): Observable<SearchResponse>;

  abstract activity(limit?: number): Observable<ActivityEvent[]>;
  abstract people(): Observable<Person[]>;
  abstract allTags(): Observable<string[]>;

  abstract createFolder(parentId: string | null, name: string): Observable<Folder>;
  abstract renameFolder(id: string, name: string): Observable<void>;
  abstract deleteFolder(id: string): Observable<void>;

  abstract uploadFiles(folderId: string, files: File[]): Observable<void>;
  abstract retryIngestion(documentId: string): Observable<void>;
  abstract toggleStar(documentId: string): Observable<void>;
  abstract moveDocument(documentId: string, folderId: string): Observable<void>;
  abstract deleteDocument(documentId: string): Observable<void>;
}
