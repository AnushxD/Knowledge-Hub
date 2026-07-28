namespace DocHub.DataAccess.Entities;

/// <summary>
/// Lifecycle of a document through the ingestion pipeline.
///
/// Only <see cref="Indexed"/> documents are visible to search and, from phase
/// 3, to the assistant — which is why the UI surfaces this state on every row
/// rather than hiding it.
/// </summary>
public enum IngestionStatus
{
    /// <summary>Stored, waiting for an ingestion worker to pick it up.</summary>
    Pending = 0,

    /// <summary>Being extracted, chunked and embedded.</summary>
    Indexing = 1,

    /// <summary>Searchable and citable.</summary>
    Indexed = 2,

    /// <summary>Ingestion failed; see FailureReason. Invisible to retrieval.</summary>
    Failed = 3,
}
