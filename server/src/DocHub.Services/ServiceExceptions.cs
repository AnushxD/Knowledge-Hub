namespace DocHub.Services;

/// <summary>
/// The requested entity does not exist. Mapped to 404 by the API's exception
/// handler, so services never reference HTTP concepts.
/// </summary>
public sealed class NotFoundException(string entity, Guid id)
    : Exception($"{entity} '{id}' was not found.")
{
    public string Entity { get; } = entity;

    public Guid Id { get; } = id;
}

/// <summary>
/// A business rule rejected the request — a duplicate folder name, an
/// oversized upload, a blocked file type. Mapped to 400 with the message shown
/// to the user, so every rule states plainly what went wrong.
/// </summary>
public sealed class ValidationException(string message) : Exception(message);
