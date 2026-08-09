namespace DocHub.Services.Repository;

/// <summary>
/// Maps a file extension onto the content type the hub serves it as.
///
/// Derived from the extension rather than taken from GitLab, which answers the
/// raw endpoint with <c>application/octet-stream</c> for everything. That would
/// be a download prompt for every PDF the preview tries to show, so the type
/// has to be decided here — and deciding it from the name means a file the hub
/// has never fetched still knows what it is.
/// </summary>
internal static class FileContentTypes
{
    /// <summary>
    /// Anything absent is served as a download. A short list on purpose: a type
    /// belongs here because a browser can be trusted to render it or because an
    /// extractor needs it, never merely because it exists.
    /// </summary>
    private static readonly Dictionary<string, string> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = "application/pdf",
            ["md"] = "text/markdown",
            ["markdown"] = "text/markdown",
            ["txt"] = "text/plain",
            ["log"] = "text/plain",
            ["csv"] = "text/csv",
            ["json"] = "application/json",
            ["xml"] = "application/xml",
            ["yml"] = "application/yaml",
            ["yaml"] = "application/yaml",
            ["html"] = "text/html",
            ["htm"] = "text/html",
            ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ["png"] = "image/png",
            ["jpg"] = "image/jpeg",
            ["jpeg"] = "image/jpeg",
            ["gif"] = "image/gif",
            ["webp"] = "image/webp",
            ["bmp"] = "image/bmp",
            ["svg"] = "image/svg+xml",
        };

    public static string For(string extension) =>
        ByExtension.GetValueOrDefault(extension, "application/octet-stream");
}
