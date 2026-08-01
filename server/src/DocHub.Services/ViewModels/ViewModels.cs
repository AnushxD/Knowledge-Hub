namespace DocHub.Services.ViewModels;

/// <summary>
/// Contracts exchanged with the outside world. Controllers accept and return
/// these; the Service layer converts them to DTOs before touching Data Access,
/// so a storage change never reshapes the public API.
/// </summary>
public record FolderViewModel(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Path,
    int DocumentCount);

public record UserViewModel(Guid Id, string Name, string Email, string Initials);

public record DocumentViewModel(
    Guid Id,
    Guid FolderId,
    string Title,
    string? Description,
    string FileName,
    string Extension,
    long SizeBytes,
    int Version,
    IReadOnlyList<string> Tags,
    UserViewModel Owner,
    string Status,
    string? FailureReason,
    int? ChunkCount,
    bool IsStarred,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record DocumentVersionViewModel(
    int Version,
    long SizeBytes,
    string? Note,
    UserViewModel ChangedBy,
    DateTimeOffset ChangedAt);

/// <param name="Sections">
/// The document's indexed chunks, in reading order. Empty until ingestion
/// succeeds — which is also when the client shows pipeline state rather than a
/// preview.
/// </param>
/// <param name="CitedInAnswers">
/// How many assistant answers cite this document, across every conversation —
/// the one measure of whether indexing it was worth anything.
/// </param>
public record DocumentDetailViewModel(
    DocumentViewModel Document,
    IReadOnlyList<FolderViewModel> Breadcrumb,
    IReadOnlyList<DocumentVersionViewModel> Versions,
    IReadOnlyList<DocumentSectionViewModel> Sections,
    int CitedInAnswers);

/// <summary>One indexed chunk, as shown in the preview and pointed at by a citation.</summary>
/// <param name="ChunkId">
/// The chunk's position in the document. Used rather than its database id
/// because it is stable in a URL (<c>/docs/:id?chunk=17</c>), readable, and
/// still unique within the document.
/// </param>
/// <param name="Heading">
/// Where in the document this came from — "Page 4", "Slide 2", a Markdown
/// heading, a worksheet name. Falls back to the position when the format
/// offers nothing.
/// </param>
public record DocumentSectionViewModel(
    int ChunkId,
    string Heading,
    string Body,
    int TokenCount);

public record LibraryStatsViewModel(
    int Documents,
    int Indexed,
    int InPipeline,
    int Failed,
    int Folders,
    long StorageBytes,
    int Chunks);

// ---- search -----------------------------------------------------------------

/// <summary>A single ranked passage, with everything a result card and a citation need.</summary>
/// <param name="ChunkId">
/// Chunk position within its document, so a result links straight to the
/// passage: <c>/docs/:documentId?chunk=:chunkId</c>.
/// </param>
/// <param name="MatchedBy">
/// Which branch found this — "keyword", "vector" or "both". Surfaced because
/// hybrid results are otherwise inexplicable to a user: it is the difference
/// between "this contains your words" and "this is about your question".
/// </param>
public record SearchResultViewModel(
    Guid DocumentId,
    string Title,
    string FileName,
    string Extension,
    Guid FolderId,
    string FolderPath,
    int ChunkId,
    string Heading,
    string Snippet,
    double Score,
    string MatchedBy);

/// <param name="Terms">
/// Normalised query words, so the client highlights exactly what was searched
/// for. Sent as data rather than as pre-marked HTML — the server never builds
/// markup the client has to trust.
/// </param>
public record SearchResponseViewModel(
    string Query,
    int TotalMatches,
    long ElapsedMs,
    IReadOnlyList<string> Terms,
    IReadOnlyList<SearchResultViewModel> Results,
    SearchDiagnosticsViewModel Diagnostics);

/// <summary>
/// How the two branches contributed. Shown in the UI and invaluable when
/// results look wrong — it separates "nothing matched" from "the embedding
/// provider is down".
/// </summary>
public record SearchDiagnosticsViewModel(
    int KeywordMatches,
    int VectorMatches,
    string EmbeddingProvider,
    bool VectorSearchAvailable,
    string? VectorSearchError);

/// <summary>A search as it arrives from the query string.</summary>
public record SearchRequest
{
    public string Query { get; init; } = string.Empty;
    public Guid? FolderId { get; init; }
    public string[]? Extension { get; init; }
    public string[]? Tag { get; init; }
    public Guid? OwnerId { get; init; }
    public int Take { get; init; } = 20;
}

// ---- activity ---------------------------------------------------------------

/// <summary>One entry in the dashboard's activity feed.</summary>
/// <param name="Type">
/// "uploaded", "indexed", "deleted" and so on. A string rather than an enum on
/// the wire: the client turns it into a verb, and an unknown value should read
/// as a generic "updated" rather than break the feed.
/// </param>
/// <param name="TargetId">
/// The document, when the entry has one, so the feed can link to it. Null for a
/// folder, and for a document that has since been deleted the link simply leads
/// to a "not found" — which is the honest outcome.
/// </param>
public record ActivityEventViewModel(
    Guid Id,
    string Type,
    UserViewModel Actor,
    string Target,
    Guid? TargetId,
    DateTimeOffset At);

// ---- authentication ---------------------------------------------------------

/// <summary>The signed-in user, as every screen needs them.</summary>
/// <param name="Role">Admin / Editor / Viewer — the client uses it to hide what cannot be done.</param>
public record SignedInUserViewModel(
    Guid Id,
    string Name,
    string Email,
    string Initials,
    string Role);

/// <summary>
/// What the login screen needs to draw itself before anyone has signed in.
/// </summary>
/// <param name="GoogleEnabled">
/// Whether to offer the Google button. Sent by the server because only the
/// server knows whether the provider is configured — a button that 404s is
/// worse than no button.
/// </param>
public record AuthOptionsViewModel(bool GoogleEnabled);

public record LoginRequest
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

/// <summary>An account as an administrator sees it on the users screen.</summary>
/// <param name="HasPassword">
/// False for an account that only signs in through Google. Shown because
/// "cannot sign in" and "signs in another way" look identical otherwise.
/// </param>
public record AccountViewModel(
    Guid Id,
    string Name,
    string Email,
    string Role,
    bool HasPassword,
    bool IsLockedOut,
    DateTimeOffset CreatedAt);

/// <summary>A new account. The password is optional for a Google-only user.</summary>
public record CreateAccountRequest
{
    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Role { get; init; } = "Viewer";

    public string? Password { get; init; }
}

public record ChangeRoleRequest
{
    public string Role { get; init; } = "Viewer";
}

// ---- knowledge sources ------------------------------------------------------

/// <summary>One body of knowledge the assistant can ground an answer in.</summary>
/// <param name="Name">Stable identifier, matching what appears in the logs.</param>
/// <param name="State">
/// "active", "inactive" or "unavailable". Sent as a state rather than as a
/// boolean because the three mean genuinely different things to a user: a
/// source that is off by design is not a source that is broken.
/// </param>
/// <param name="Detail">Why it is in that state, in one actionable sentence.</param>
public record KnowledgeSourceViewModel(
    string Name,
    string DisplayName,
    string Description,
    string State,
    string Detail);

/// <summary>One repository source's address, as an administrator manages it.</summary>
/// <param name="Name">
/// The stable identifier, which is also what addresses it in the API's routes.
/// </param>
/// <param name="DisplayName">What to call it on screen.</param>
/// <param name="IsFromConfiguration">
/// True when no override is stored and the deployment's own configuration is in
/// effect. The screen says so, because an administrator editing a field that
/// configuration is supplying otherwise has no way to tell why nothing changed.
/// </param>
public record RepositorySourceViewModel(
    string Name,
    string DisplayName,
    string? Endpoint,
    bool IsEnabled,
    bool IsFromConfiguration,
    DateTimeOffset? UpdatedAt);

public record UpdateRepositorySourceRequest
{
    /// <summary>Absolute http/https address. Empty switches the source off without losing it.</summary>
    public string? Endpoint { get; init; }

    public bool IsEnabled { get; init; }
}

/// <summary>The outcome of probing an address before saving it.</summary>
/// <param name="Detail">
/// Says what was actually established. A reachable address is not the same as a
/// working MCP server, and the wording must not imply otherwise.
/// </param>
public record RepositoryProbeViewModel(bool IsReachable, string Detail);

// ---- chat -------------------------------------------------------------------

/// <summary>A source backing an answer, resolvable to the exact passage.</summary>
/// <param name="Marker">The bracketed number used in the answer text.</param>
/// <param name="Kind">
/// "document" or "external". The client links a document citation into the hub
/// and an external one out to <paramref name="Url"/>, so it has to be told
/// which it is rather than guessing from a null id.
/// </param>
/// <param name="DocumentId">Null for an external citation.</param>
/// <param name="Url">Null when the source could not supply a link.</param>
/// <param name="SourceName">Which knowledge source this came from.</param>
public record CitationViewModel(
    int Marker,
    string Kind,
    string Title,
    int ChunkId,
    string Heading,
    Guid? DocumentId = null,
    string? Url = null,
    string? SourceName = null);

public record ChatSessionViewModel(
    Guid Id,
    string Title,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="IsRefusal">
/// True when the assistant declined for lack of grounding. The client renders
/// this very differently from an answer — it is a designed outcome, not a
/// failure.
/// </param>
/// <param name="Degradations">
/// Sources that could not be searched when this answer was given. Persisted
/// with the message, so reopening the conversation still says the grounding was
/// thinner than usual.
/// </param>
public record ChatMessageViewModel(
    Guid Id,
    string Role,
    string Content,
    IReadOnlyList<CitationViewModel> Citations,
    bool IsRefusal,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Degradations);

public record ChatTranscriptViewModel(
    ChatSessionViewModel Session,
    IReadOnlyList<ChatMessageViewModel> Messages);

/// <summary>A question, optionally continuing an existing conversation.</summary>
public record AskRequest
{
    public string Question { get; init; } = string.Empty;

    /// <summary>Null starts a new conversation.</summary>
    public Guid? SessionId { get; init; }

    /// <summary>Restricts retrieval to a folder and its subtree.</summary>
    public Guid? FolderId { get; init; }
}

// ---- requests ---------------------------------------------------------------

public record CreateFolderRequest(Guid? ParentId, string Name);

public record RenameFolderRequest(string Name);

public record UpdateDocumentRequest(
    string? Title,
    string? Description,
    IReadOnlyList<string>? Tags,
    bool? IsStarred);

public record MoveDocumentRequest(Guid FolderId);

/// <summary>
/// An uploaded file, decoupled from ASP.NET Core. The Service layer takes a
/// plain stream so it never depends on IFormFile — that keeps services testable
/// without spinning up the web stack.
/// </summary>
public record UploadRequest(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Note = null);

/// <summary>Filter for a document listing, as it arrives from the query string.</summary>
public record DocumentQueryRequest
{
    public Guid? FolderId { get; init; }
    public bool Recursive { get; init; } = true;
    public string? Text { get; init; }
    public string[]? Status { get; init; }
    public string[]? Extension { get; init; }
    public string[]? Tag { get; init; }
    public Guid? OwnerId { get; init; }
    public bool StarredOnly { get; init; }
    public string? Sort { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 200;
}

/// <summary>A file being streamed back to the caller.</summary>
public record DocumentContent(Stream Content, string ContentType, string FileName, long SizeBytes);
