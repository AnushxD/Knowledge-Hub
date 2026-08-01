namespace DocHub.DataAccess;

/// <summary>
/// Fits a string to a bounded column.
///
/// For text that comes from outside — a heading someone wrote, an exception
/// message — a length limit is a fact about storage, not about the input, and
/// the caller usually has nothing better to do with an over-long value than
/// shorten it. Losing a document because the label on one of its chunks ran
/// long is the outcome this exists to prevent.
/// </summary>
public static class Truncate
{
    /// <summary>
    /// <paramref name="value"/> cut to at most <paramref name="maxLength"/>
    /// characters, ending in an ellipsis when anything was dropped so a reader
    /// can tell a shortened value from a short one.
    /// </summary>
    /// <param name="maxLength">
    /// The column's length. Must be at least 2, or there is no room for both a
    /// character and the ellipsis that says characters are missing.
    /// </param>
    public static string? ToFit(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);

        if (value is null || value.Length <= maxLength) return value;

        var cut = maxLength - 1;

        // Never cut between the halves of a surrogate pair: a lone surrogate is
        // not valid UTF-16, and the driver rejects it on the way out. Postgres
        // counts characters where .NET counts UTF-16 units, so dropping one more
        // unit is always safe — it can only make the value shorter than the
        // column, never longer.
        if (char.IsHighSurrogate(value[cut - 1])) cut--;

        return string.Concat(value.AsSpan(0, cut), "…");
    }
}
