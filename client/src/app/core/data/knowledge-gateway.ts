import { Observable } from 'rxjs';
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
  Folder,
  KnowledgeSource,
  LibraryStats,
  Person,
  RepositoryProbe,
  RepositorySource,
  SearchQuery,
  SearchResponse,
  SignedInUser,
  UserRole,
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
  /**
   * The signed-in user, or null when the session is gone.
   *
   * Null rather than an error, because "nobody is signed in" is the normal
   * state on first load and not something to render as a failure.
   */
  abstract currentUser(): Observable<SignedInUser | null>;

  abstract authOptions(): Observable<AuthOptions>;

  abstract signIn(email: string, password: string): Observable<SignedInUser>;

  abstract signOut(): Observable<void>;

  // ---- account administration (Admin only; the API enforces it) ------------

  abstract accounts(): Observable<Account[]>;

  abstract createAccount(input: NewAccount): Observable<Account>;

  abstract changeAccountRole(id: string, role: UserRole): Observable<Account>;

  abstract setAccountEnabled(id: string, enabled: boolean): Observable<Account>;

  abstract folders(): Observable<Folder[]>;
  abstract documents(query: DocumentQuery): Observable<DocumentSummary[]>;
  abstract document(id: string): Observable<DocumentDetail | undefined>;
  abstract stats(): Observable<LibraryStats>;

  /**
   * The stored file decoded as text, for previewing Markdown, code and plain
   * text as themselves rather than as extracted chunks.
   */
  abstract documentText(id: string): Observable<string>;

  /**
   * A URL a frame or `<img>` can point at to display the stored file, for the
   * types the browser renders better than we could — PDFs and raster images.
   *
   * Null when the implementation has no real file behind the document, which
   * the preview reports rather than rendering an empty frame.
   */
  abstract documentContentUrl(id: string): string | null;

  /**
   * Hybrid keyword + semantic search over indexed chunks. Distinct from
   * `documents()`, which filters the library by metadata — this searches
   * inside the content.
   */
  abstract search(query: SearchQuery): Observable<SearchResponse>;

  /**
   * Asks the assistant, emitting each server-sent event as it arrives.
   *
   * Streaming rather than a single response because a grounded answer takes
   * seconds to generate, and the retrieved sources are worth showing before
   * the first word of the answer exists.
   */
  abstract ask(request: AskRequest): Observable<ChatEvent>;

  /**
   * The bodies of knowledge the assistant may ground an answer in, and whether
   * each is contributing right now. Reported rather than searched — the screen
   * must not have to ask a question to draw itself.
   */
  abstract knowledgeSources(): Observable<KnowledgeSource[]>;

  // ---- repository source administration (Admin only; the API enforces it) ---

  abstract repositorySource(): Observable<RepositorySource>;

  abstract saveRepositorySource(
    endpoint: string | null,
    isEnabled: boolean,
  ): Observable<RepositorySource>;

  /** Drops the override so configuration applies again. */
  abstract resetRepositorySource(): Observable<RepositorySource>;

  abstract testRepositorySource(endpoint: string | null): Observable<RepositoryProbe>;

  abstract chatSessions(): Observable<ChatSession[]>;
  abstract chatTranscript(sessionId: string): Observable<ChatTranscript>;
  abstract deleteChatSession(sessionId: string): Observable<void>;

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
