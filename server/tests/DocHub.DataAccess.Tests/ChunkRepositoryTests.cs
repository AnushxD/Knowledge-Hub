using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;

namespace DocHub.DataAccess.Tests;

/// <summary>
/// The relevance floor on the vector branch, against real pgvector.
///
/// Tested here rather than through the Service layer because the Service tests
/// run on hashing embeddings, whose vectors have no geometry — a floor over
/// them would measure nothing. These vectors are chosen so the distances are
/// arithmetic rather than a matter of opinion.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ChunkRepositoryTests(PostgresFixture fixture)
{
    private static readonly Guid Owner = DocHubDbContext.SystemUserId;

    /// <summary>A unit vector along one axis, so two of them are exactly orthogonal.</summary>
    private static float[] Axis(int index)
    {
        var vector = new float[DocHubDbContext.EmbeddingDimensions];
        vector[index] = 1f;
        return vector;
    }

    [Fact]
    public async Task A_chunk_beyond_the_distance_floor_is_not_returned()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);
        var chunks = new ChunkRepository(db);

        var folder = await folders.CreateAsync(null, $"Floor-{Guid.NewGuid():N}"[..20], Owner);

        var document = await documents.CreateAsync(new NewDocumentDto
        {
            FolderId = folder.Id,
            Title = "Floor",
            FileName = "floor.md",
            Extension = "md",
            ContentType = "text/markdown",
            SizeBytes = 32,
            StoragePath = $"documents/{Guid.NewGuid():N}.md",
            OwnerId = Owner,
            Tags = [],
        });

        await documents.SetStatusAsync(document.Id, IngestionStatus.Indexed, chunkCount: 2);

        // Identical to the query, and orthogonal to it: cosine distance 0 and 1.
        await chunks.ReplaceAsync(document.Id, document.Version, [
            new NewChunkDto(0, "The near one.", "near", 3, Axis(0)),
            new NewChunkDto(1, "The far one.", "far", 3, Axis(1)),
        ]);

        // Scoped to this test's own folder: the suite shares a database, so an
        // unscoped vector search would find every other test's chunks too.
        var query = new ChunkSearchDto { Text = "anything", Limit = 10, FolderId = folder.Id };

        // Without a floor the far chunk comes back, because a nearest-neighbour
        // search always has a nearest neighbour however far away it is. That is
        // what let an orange-juice question retrieve a payments document.
        var unfiltered = await chunks.SearchVectorAsync(query, Axis(0));
        Assert.Equal(2, unfiltered.Count);

        var floored = await chunks.SearchVectorAsync(
            query with { MaxDistance = 0.5 }, Axis(0));

        var match = Assert.Single(floored);
        Assert.Equal("near", match.SectionRef);
    }

    [Fact]
    public async Task A_floor_that_excludes_everything_returns_nothing_rather_than_the_least_bad()
    {
        await using var db = fixture.CreateContext();
        var folders = new FolderRepository(db);
        var documents = new DocumentRepository(db);
        var chunks = new ChunkRepository(db);

        var folder = await folders.CreateAsync(null, $"None-{Guid.NewGuid():N}"[..20], Owner);

        var document = await documents.CreateAsync(new NewDocumentDto
        {
            FolderId = folder.Id,
            Title = "None",
            FileName = "none.md",
            Extension = "md",
            ContentType = "text/markdown",
            SizeBytes = 32,
            StoragePath = $"documents/{Guid.NewGuid():N}.md",
            OwnerId = Owner,
            Tags = [],
        });

        await documents.SetStatusAsync(document.Id, IngestionStatus.Indexed, chunkCount: 1);

        await chunks.ReplaceAsync(document.Id, document.Version, [
            new NewChunkDto(0, "Nothing like the question.", "far", 4, Axis(1)),
        ]);

        // Empty is the honest answer to a question this corpus cannot address,
        // and it is what makes the assistant refuse instead of inventing.
        var floored = await chunks.SearchVectorAsync(
            new ChunkSearchDto
            {
                Text = "anything",
                Limit = 10,
                FolderId = folder.Id,
                MaxDistance = 0.5,
            },
            Axis(0));

        Assert.Empty(floored);
    }
}
