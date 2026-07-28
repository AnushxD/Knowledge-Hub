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

    public DbSet<User> Users => Set<User>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
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
    }
}
