namespace DocHub.Services;

/// <summary>
/// The requested entity does not exist. Mapped to 404 by the API's exception
/// handler, so services never reference HTTP concepts.
/// </summary>
public sealed class NotFoundException(string entity, string key)
    : Exception($"{entity} '{key}' was not found.")
{
    /// <summary>Most entities are keyed by id; a few, like a knowledge source, by name.</summary>
    public NotFoundException(string entity, Guid id)
        : this(entity, id.ToString())
    {
    }

    public string Entity { get; } = entity;

    public string Key { get; } = key;
}

/// <summary>
/// A business rule rejected the request — a duplicate folder name, an
/// oversized upload, a blocked file type. Mapped to 400 with the message shown
/// to the user, so every rule states plainly what went wrong.
/// </summary>
public sealed class ValidationException(string message) : Exception(message);
