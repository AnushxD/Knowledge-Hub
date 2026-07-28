namespace DocHub.Integrations.Embeddings;

/// <summary>
/// An embedding call failed.
///
/// Distinct from a generic exception so the ingestion job can tell "the model
/// is unavailable, worth retrying" apart from "this file cannot be parsed and
/// never will be" — the two deserve different retry behaviour and different
/// wording in front of the user.
/// </summary>
public sealed class EmbeddingException(string message) : Exception(message);
