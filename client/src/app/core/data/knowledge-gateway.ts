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
  KnowledgeSourceSummary,
  LibraryStats,
  Repository,
  RepositoryConnection,
  RepositoryProbe,
  RepositorySettings,
  RepositorySettingsDraft,
  RepositorySource,
  RepositorySourceDraft,
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

  /**
   * Changes the signed-in user's own password. No id: the server changes
   * whoever is asking, so there is nothing here to point at someone else.
   */
  abstract changePassword(currentPassword: string, newPassword: string): Observable<void>;

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
   * A URL that saves the stored file rather than displaying it.
   *
   * Separate from `documentContentUrl` because the two want opposite
   * dispositions from the same endpoint, and a caller should not have to know
   * which query parameter produces which.
   *
   * Given to an anchor rather than fetched: the browser streams straight to
   * disk, which costs no memory on a large file, shows native download
   * progress, and takes the file name from the server's Content-Disposition.
   *
   * Null when the implementation has no real file behind the document, so the
   * control can be left out rather than offered and doing nothing.
   */
  abstract documentDownloadUrl(id: string): string | null;

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
  abstract knowledgeSources(): Observable<KnowledgeSourceSummary[]>;

  /**
   * Each source's live state. Separate from the list because it costs a round
   * trip to every remote server, and the screen should not wait on that to
   * appear.
   */
  abstract knowledgeSourceStatuses(): Observable<KnowledgeSource[]>;

  // ---- repository source administration (Admin only; the API enforces it) ---

  /** Every MCP repository server that has been added, oldest first. */
  abstract repositorySources(): Observable<RepositorySource[]>;

  /** Adds a server. It is searched from the next question onwards. */
  abstract addRepositorySource(
    name: string,
    draft: RepositorySourceDraft,
  ): Observable<RepositorySource>;

  /** Changes everything about a server except its name, which is its identity. */
  abstract saveRepositorySource(
    name: string,
    draft: RepositorySourceDraft,
  ): Observable<RepositorySource>;

  /** Removes it entirely. Switching it off instead keeps its address. */
  abstract removeRepositorySource(name: string): Observable<void>;

  /** Takes an address rather than a name — the moment to test one is before it exists. */
  abstract testRepositorySource(endpoint: string): Observable<RepositoryProbe>;

  abstract chatSessions(): Observable<ChatSession[]>;
  abstract chatTranscript(sessionId: string): Observable<ChatTranscript>;
  abstract deleteChatSession(sessionId: string): Observable<void>;

  abstract activity(limit?: number): Observable<ActivityEvent[]>;
  abstract allTags(): Observable<string[]>;

  // ---- the mirrored repository --------------------------------------------

  /**
   * Where the library comes from and how current it is.
   *
   * Cheap by design — one row and configuration, no call to GitLab — so a
   * screen can poll it while a sync runs.
   */
  abstract repository(): Observable<Repository>;

  /**
   * Queues a sync and returns the state as it stands. Admin only; the API
   * enforces it.
   *
   * The mirror is not current when this resolves — a full sync runs for
   * minutes on a background worker — so the caller polls `repository()` rather
   * than treating the response as the result.
   */
  abstract syncRepository(): Observable<Repository>;

  // ---- pointing the hub at a repository (Admin only; the API enforces it) ---

  /**
   * The repository settings in force. Secrets are described, never returned:
   * there is nothing a screen can do with a token it already holds.
   */
  abstract repositorySettings(): Observable<RepositorySettings>;

  /**
   * Saves the settings. In force immediately — the next sync, webhook and file
   * fetch use them — but nothing is mirrored until a sync is asked for.
   */
  abstract saveRepositorySettings(draft: RepositorySettingsDraft): Observable<RepositorySettings>;

  /** Reads the repository described by the draft without saving it. */
  abstract testRepositorySettings(draft: RepositorySettingsDraft): Observable<RepositoryConnection>;

  abstract retryIngestion(documentId: string): Observable<void>;
  abstract toggleStar(documentId: string): Observable<void>;

  // There is no createFolder, uploadFiles, moveDocument or deleteDocument.
  // The repository is the system of record: a document exists because a file
  // does, and the only thing that changes that is a commit.
}
