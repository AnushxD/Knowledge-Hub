using System.Text.Json;
using System.Text.Json.Serialization;
using DocHub.DataAccess.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DocHub.DataAccess;

/// <summary>
/// EF Core context for the relational store. Internal on purpose: nothing
/// outside this layer touches EF Core, so Services can only reach data through
/// the repository interfaces.
/// </summary>
public sealed class DocHubDbContext(DbContextOptions<DocHubDbContext> options)
    : IdentityUserContext<User, Guid>(options)
{
    /// <summary>Deterministic id for the seeded local development user.</summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-0000000000a1");

    /// <summary>The seeded administrator's sign-in address.</summary>
    public const string SystemUserEmail = "dev@dochub.local";

    /// <summary>
    /// Width of the embedding column, fixed by the migration. Matches
    /// nomic-embed-text, the default local provider. A provider with a
    /// different width needs a new migration and a full re-index, so the
    /// Integrations layer validates its own dimension against this at startup
    /// rather than failing per-row at write time.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    /// <summary>
    /// Text search configuration used for both the generated tsvector column
    /// and every query against it. They have to agree — stemming differences
    /// between index and query silently return nothing.
    /// </summary>
    public const string SearchConfiguration = "english";

    // Users is not declared here: IdentityUserContext already exposes it as
    // DbSet<User>, and re-declaring it shadowed the base property rather than
    // replacing it — two ways to reach one table, only one of which Identity's
    // own stores use.

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    public DbSet<RepositorySourceSetting> RepositorySourceSettings =>
        Set<RepositorySourceSetting>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity configures the user store's own keys, indexes and columns
        // first; everything below either adds to that or renames it.
        base.OnModelCreating(builder);

        // Enables the vector type used by document_chunks.embedding.
        builder.HasPostgresExtension("vector");

        // Identity's tables default to PascalCase "AspNet…" names. Renamed to
        // match every other table here — a database dump should not show which
        // framework wrote which half of it.
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Name).HasMaxLength(200).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(32).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();

            // The seeded administrator, so a fresh database has someone who can
            // sign in and create everyone else.
            //
            // Every value here is a fixed constant, including the stamps: EF
            // compares seed data to decide whether a migration is needed, so a
            // generated stamp would make each scaffold produce a spurious
            // update. The password hash is deliberately absent — a hash is
            // salted per call and could not be a constant, and baking a
            // credential into a migration would put one in source control.
            // `dotnet run -- seed-admin` sets it, in keeping with this
            // project's rule that provisioning is explicit.
            entity.HasData(new User
            {
                Id = SystemUserId,
                Name = "Local Developer",
                Email = SystemUserEmail,
                NormalizedEmail = SystemUserEmail.ToUpperInvariant(),
                UserName = SystemUserEmail,
                NormalizedUserName = SystemUserEmail.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = "5f1b0d5a-6c1e-4f27-9f4e-6d2c9d0a71b3",
                ConcurrencyStamp = "0f8c5b2d-1a44-4a1f-8c0e-3b6d7a9e2f45",
                Role = Roles.Admin,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            });
        });

        builder.Entity<ActivityEvent>(entity =>
        {
            entity.ToTable("activity_events");
            entity.HasKey(activity => activity.Id);
            entity.Property(activity => activity.Target).HasMaxLength(500).IsRequired();

            // Text, like every other enum here, so reordering the C# values
            // cannot silently remap history.
            entity.Property(activity => activity.Type)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.HasOne(activity => activity.Actor)
                .WithMany()
                .HasForeignKey(activity => activity.ActorId)
                // Accounts are disabled rather than deleted, so this never
                // fires — but if one ever were, losing the audit trail with it
                // is the opposite of what an audit trail is for.
                .OnDelete(DeleteBehavior.Restrict);

            // The feed only ever asks for the newest few.
            entity.HasIndex(activity => activity.At).IsDescending();
        });

        builder.Entity<RepositorySourceSetting>(entity =>
        {
            entity.ToTable("repository_source_settings");
            entity.HasKey(setting => setting.Name);
            entity.Property(setting => setting.Name).HasMaxLength(64);
            // Long enough for a hostname with a path; not unbounded, because an
            // endpoint is a URL and an unbounded column invites pasting a page
            // into it.
            entity.Property(setting => setting.Endpoint).HasMaxLength(2000);

            // Deliberately no seed row. Its absence is meaningful: it means
            // nobody has overridden configuration, which is a different state
            // from "an administrator set it to empty".
        });

        builder.Entity<Folder>(entity =>
        {
            entity.ToTable("folders");
            entity.HasKey(folder => folder.Id);
            entity.Property(folder => folder.Name).HasMaxLength(200).IsRequired();
            entity.Property(folder => folder.Path).HasMaxLength(2000).IsRequired();

            entity.HasOne(folder => folder.Parent)
                .WithMany(folder => folder.Children)
                .HasForeignKey(folder => folder.ParentId)
                // Deleting a folder deletes its subtree; the service layer
                // decides whether that is allowed before calling delete.
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(folder => folder.Owner)
                .WithMany(user => user.Folders)
                .HasForeignKey(folder => folder.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(folder => folder.ParentId);
            // Sibling names must be unique, so a path always identifies one folder.
            entity.HasIndex(folder => new { folder.ParentId, folder.Name }).IsUnique();
            entity.HasIndex(folder => folder.Path);
        });

        builder.Entity<Document>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Title).HasMaxLength(500).IsRequired();
            entity.Property(document => document.Description).HasMaxLength(4000);
            entity.Property(document => document.FileName).HasMaxLength(500).IsRequired();
            entity.Property(document => document.Extension).HasMaxLength(32).IsRequired();
            entity.Property(document => document.ContentType).HasMaxLength(200).IsRequired();
            entity.Property(document => document.StoragePath).HasMaxLength(1000).IsRequired();
            entity.Property(document => document.FailureReason).HasMaxLength(2000);

            // Stored as text rather than an int: a dump stays readable, and
            // reordering the enum can never silently remap existing rows.
            entity.Property(document => document.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            // Npgsql maps string[] to a native text[] column.
            entity.Property(document => document.Tags).HasColumnType("text[]");

            entity.HasOne(document => document.Folder)
                .WithMany(folder => folder.Documents)
                .HasForeignKey(document => document.FolderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(document => document.Owner)
                .WithMany(user => user.Documents)
                .HasForeignKey(document => document.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(document => document.FolderId);
            entity.HasIndex(document => document.Status);
            entity.HasIndex(document => document.UpdatedAt);
            // GIN index so tag filtering stays fast as the library grows.
            entity.HasIndex(document => document.Tags).HasMethod("gin");
        });

        builder.Entity<DocumentVersion>(entity =>
        {
            entity.ToTable("document_versions");
            entity.HasKey(version => version.Id);
            entity.Property(version => version.StoragePath).HasMaxLength(1000).IsRequired();
            entity.Property(version => version.Note).HasMaxLength(1000);

            entity.HasOne(version => version.Document)
                .WithMany(document => document.Versions)
                .HasForeignKey(version => version.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(version => version.ChangedBy)
                .WithMany()
                .HasForeignKey(version => version.ChangedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(version => new { version.DocumentId, version.VersionNumber }).IsUnique();
        });

        builder.Entity<DocumentChunk>(entity =>
        {
            entity.ToTable("document_chunks");
            entity.HasKey(chunk => chunk.Id);
            entity.Property(chunk => chunk.Text).IsRequired();
            entity.Property(chunk => chunk.SectionRef).HasMaxLength(300);

            entity.Property(chunk => chunk.Embedding)
                .HasColumnType($"vector({EmbeddingDimensions})")
                .IsRequired();

            // Maintained by Postgres, so the index can never drift from the
            // text the way an application-populated column would.
            entity.HasGeneratedTsVectorColumn(
                    chunk => chunk.SearchVector!,
                    SearchConfiguration,
                    chunk => chunk.Text)
                .HasIndex(chunk => chunk.SearchVector)
                .HasMethod("gin");

            entity.HasOne(chunk => chunk.Document)
                .WithMany(document => document.Chunks)
                .HasForeignKey(chunk => chunk.DocumentId)
                // Deleting a document must take its chunks with it, or deleted
                // content stays answerable through search.
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(chunk => new { chunk.DocumentId, chunk.Ordinal }).IsUnique();

            // HNSW over cosine distance: the embedding providers all return
            // normalised vectors, and cosine is what the search service ranks
            // on. Built here rather than left to a manual DBA step so a fresh
            // clone gets the same query plan as production.
            entity.HasIndex(chunk => chunk.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        builder.Entity<ChatSession>(entity =>
        {
            entity.ToTable("chat_sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Title).HasMaxLength(300).IsRequired();

            entity.HasOne(session => session.User)
                .WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // History is always read newest-first for one user.
            entity.HasIndex(session => new { session.UserId, session.UpdatedAt });
        });

        builder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Content).IsRequired();

            // Text for the same reason as IngestionStatus: a dump stays
            // readable and reordering the enum cannot remap existing rows.
            entity.Property(message => message.Role)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            entity.Property(message => message.Citations)
                .HasConversion(CitationsConverter)
                .Metadata.SetValueComparer(CitationsComparer);

            entity.Property(message => message.Citations).HasColumnType("jsonb");

            // Counting the answers that cite one document is a containment test
            // over every message. jsonb_path_ops rather than the default
            // operator class: it indexes only `@>`, which is the single way this
            // column is ever searched, and is smaller and faster for it.
            entity.HasIndex(message => message.Citations)
                .HasMethod("gin")
                .HasOperators("jsonb_path_ops");

            // Same jsonb treatment as citations, and for the same reason: read
            // whole with the message, never queried across.
            entity.Property(message => message.Degradations)
                .HasConversion(DegradationsConverter)
                .Metadata.SetValueComparer(DegradationsComparer);

            entity.Property(message => message.Degradations)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb");

            entity.HasOne(message => message.Session)
                .WithMany(session => session.Messages)
                .HasForeignKey(message => message.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(message => new { message.SessionId, message.CreatedAt });
        });
    }

    private static readonly JsonSerializerOptions CitationJson = new(JsonSerializerDefaults.Web)
    {
        // CitationKind as "document"/"external", never 0/1 — the same reasoning
        // as HasConversion<string>() on the column enums. A number in stored
        // jsonb would be remapped by anyone reordering the enum, silently
        // rewriting what old answers claim to have cited.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Citations round-trip through jsonb. Explicit rather than relying on
    /// Npgsql's dynamic JSON mapping, which needs an opt-in at the data source
    /// and fails at run time rather than at model build if it is missing.
    /// </summary>
    private static readonly ValueConverter<IReadOnlyList<Citation>, string> CitationsConverter =
        new(
            citations => JsonSerializer.Serialize(citations, CitationJson),
            json => JsonSerializer.Deserialize<List<Citation>>(json, CitationJson)
                ?? new List<Citation>());

    private static readonly ValueConverter<IReadOnlyList<string>, string> DegradationsConverter =
        new(
            degradations => JsonSerializer.Serialize(degradations, CitationJson),
            json => JsonSerializer.Deserialize<List<string>>(json, CitationJson)
                ?? new List<string>());

    private static readonly ValueComparer<IReadOnlyList<string>> DegradationsComparer =
        new(
            (left, right) => left!.SequenceEqual(right!),
            degradations => degradations.Aggregate(
                0, (hash, entry) => HashCode.Combine(hash, entry.GetHashCode())),
            degradations => degradations.ToList());

    /// <summary>
    /// Without this EF compares the list by reference and never notices an
    /// edit, so a changed citation set would silently not be saved.
    /// </summary>
    private static readonly ValueComparer<IReadOnlyList<Citation>> CitationsComparer =
        new(
            (left, right) => left!.SequenceEqual(right!),
            citations => citations.Aggregate(
                0, (hash, citation) => HashCode.Combine(hash, citation.GetHashCode())),
            citations => citations.ToList());
}
