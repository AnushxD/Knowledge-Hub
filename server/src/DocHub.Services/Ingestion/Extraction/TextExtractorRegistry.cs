namespace DocHub.Services.Ingestion.Extraction;

/// <summary>
/// Maps an extension to the extractor that handles it.
///
/// Built from whatever <see cref="ITextExtractor"/> implementations are
/// registered, so supporting a new format is one class plus one registration —
/// nothing here or in the ingestion service changes.
/// </summary>
internal sealed class TextExtractorRegistry : ITextExtractorRegistry
{
    private readonly Dictionary<string, ITextExtractor> byExtension;

    public TextExtractorRegistry(IEnumerable<ITextExtractor> extractors)
    {
        byExtension = new Dictionary<string, ITextExtractor>(StringComparer.OrdinalIgnoreCase);

        foreach (var extractor in extractors)
        {
            foreach (var extension in extractor.Extensions)
            {
                // Two extractors claiming one extension is a registration bug,
                // and silently picking one would make which parser ran depend
                // on DI ordering.
                if (!byExtension.TryAdd(extension, extractor))
                {
                    throw new InvalidOperationException(
                        $"Both {byExtension[extension].GetType().Name} and "
                        + $"{extractor.GetType().Name} claim the '.{extension}' extension.");
                }
            }
        }

        SupportedExtensions = [.. byExtension.Keys.OrderBy(extension => extension)];
    }

    public IReadOnlyList<string> SupportedExtensions { get; }

    public ITextExtractor? Find(string extension) =>
        byExtension.GetValueOrDefault(extension.TrimStart('.'));
}
