namespace TaskFlow.Application.TaskManagement.Search;

/// <summary>
/// Query normalization + ranking for the command palette search (slice 013, research D2/D3, data-model.md):
/// whitespace tokens (≤ 5, each ≤ 100 chars) escaped for a literal <c>ILIKE '%token%'</c> match, and the
/// in-memory rank key applied to the capped candidate set the repositories return.
/// </summary>
public static class SearchTokens
{
    /// <summary>The most tokens a query contributes (extra tokens are ignored, never an error).</summary>
    public const int MaxTokens = 5;

    /// <summary>The longest single token used for matching.</summary>
    public const int MaxTokenLength = 100;

    /// <summary>Splits and normalizes the raw query into upper-cased (case-folded) tokens (empty for a blank query).</summary>
    /// <param name="q">The raw query string (already length-validated at the boundary).</param>
    public static IReadOnlyList<string> Tokenize(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return [];
        }

        return q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxTokens)
            .Select(t => t.Length > MaxTokenLength ? t[..MaxTokenLength] : t)
            .Select(t => t.ToUpperInvariant())
            .ToList();
    }

    /// <summary>
    /// The <c>ILIKE</c> pattern for a token: LIKE metacharacters escaped (backslash, the Npgsql default) so the
    /// user's text is matched literally, never as a wildcard (Constitution XII — the <c>UserRepository</c> precedent).
    /// </summary>
    /// <param name="token">A normalized token.</param>
    public static string ContainsPattern(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var escaped = token
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return "%" + escaped + "%";
    }

    /// <summary>
    /// The rank of a candidate text against the whole (case-folded, space-joined) query: 0 exact, 1 prefix,
    /// 2 word-start, 3 substring/other (data-model.md). Lower sorts first.
    /// </summary>
    /// <param name="text">The candidate's primary text (title / name).</param>
    /// <param name="query">The normalized query (tokens joined by one space).</param>
    public static int Rank(string? text, string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrEmpty(text) || query.Length == 0)
        {
            return 3;
        }

        var lower = text.ToUpperInvariant();
        if (lower == query)
        {
            return 0;
        }

        if (lower.StartsWith(query, StringComparison.Ordinal))
        {
            return 1;
        }

        if (lower.Contains(" " + query, StringComparison.Ordinal))
        {
            return 2;
        }

        return 3;
    }
}
