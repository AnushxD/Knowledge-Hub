using DocHub.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess;

/// <summary>
/// EF Core context for the relational store. Internal on purpose: nothing
/// outside this layer touches EF Core, so Services can only reach data through
/// the repository interfaces.
/// </summary>
public sealed class DocHubDbContext(DbContextOptions<DocHubDbContext> options) : DbContext(options)
{
    /// <summary>Deterministic id for the seeded local development user.</summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-0000000000a1");

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

    public DbSet<User> Users => Set<User>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Enables the vector type used by document_chunks.embedding.
        builder.HasPostgresExtension("vector");

        builder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Name).HasMaxLength(200).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(32).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();

            // Seeded so phase 1 has an owner for every folder and document.
            // Phase 5 replaces this with real authenticated principals.
            entity.HasData(new User
            {
                Id = SystemUserId,
                Name = "Local Developer",
                Email = "dev@dochub.local",
                Role = "Admin",
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            });
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
    }
}
