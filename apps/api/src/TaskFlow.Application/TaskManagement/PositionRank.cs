using System.Text.RegularExpressions;
using FluentValidation;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The shared fractional-indexing rank ("position") FORMAT rule, reused by create
/// (<see cref="CreateTaskValidator"/>) and reorder (slice-002 T048) so both enforce one
/// identical definition of a well-formed rank (research R5). The server is a format-VALIDATOR
/// only — it never generates ranks; the client is the sole rank generator and the server the
/// sole writer under the <c>version</c> guard.
/// </summary>
/// <remarks>
/// The alphabet is pinned IDENTICALLY to the client's <c>fractional-indexing</c>
/// <c>BASE_62_DIGITS</c> (<c>0-9A-Za-z</c>, ascending charcode order) — see
/// <c>apps/web/src/lib/position.ts</c>. That ascending-charcode alphabet coincides exactly with
/// the server's byte-ordinal <c>COLLATE "C"</c> sort, so client and server agree on order. A rank
/// is therefore valid iff it is a non-empty string drawn solely from that alphabet.
/// </remarks>
public static partial class PositionRank
{
    /// <summary>The pinned rank alphabet, matching the client <c>BASE_62_DIGITS</c> (<c>0-9A-Za-z</c>).</summary>
    [GeneratedRegex("^[0-9A-Za-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RankFormat();

    /// <summary>True iff <paramref name="position"/> is a non-empty, well-formed fractional-indexing rank.</summary>
    public static bool IsValid(string? position) =>
        !string.IsNullOrEmpty(position) && RankFormat().IsMatch(position);

    /// <summary>
    /// FluentValidation rule extension applying the shared <see cref="IsValid"/> format check to a
    /// <c>position</c> property, surfacing a <c>422 validation_failed</c> on a malformed/empty rank.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ValidPositionRank<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        return ruleBuilder
            .Must(IsValid)
            .WithMessage("Position must be a non-empty fractional-indexing rank (characters 0-9, A-Z, a-z).");
    }

    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// A rank strictly AFTER <paramref name="source"/> and strictly BEFORE
    /// <paramref name="successor"/> (byte-ordinal order) — the DuplicateTask adjacency
    /// (slice 019, D7: the duplicate lands directly after its source). This is the ONE
    /// deliberate exception to the "server never generates ranks" rule: the duplicate
    /// contract pins adjacency server-side (contracts/task-duplicate.md) and the client
    /// supplies only <c>newTaskId</c>. Create/reorder remain client-authoritative.
    /// </summary>
    /// <remarks>
    /// The result always EXTENDS the source key (source + a fraction suffix), which keeps it a
    /// valid fractional-indexing key for the client's later <c>between()</c> calls: when the
    /// successor is absent or shares no prefix with the source, any extension of the source sorts
    /// strictly between them; when the successor extends the source, the suffix is the midpoint
    /// of the empty fraction and the successor's remainder (the library's fraction-midpoint rule).
    /// </remarks>
    public static string BetweenAfter(string source, string? successor)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        if (successor is not null && string.CompareOrdinal(source, successor) >= 0)
        {
            throw new ArgumentException("successor must sort strictly after source.", nameof(successor));
        }

        if (successor is null || !successor.StartsWith(source, StringComparison.Ordinal))
        {
            // No successor, or the successor diverges before the end of the source — every
            // extension of the source stays strictly below it. 'V' is the mid digit.
            return source + "V";
        }

        // The successor extends the source: append the midpoint between the empty fraction
        // ("just after source") and the successor's remaining digits.
        return source + MidFraction("", successor[source.Length..]);
    }

    /// <summary>Shortest digit string strictly between fractions <paramref name="a"/> and <paramref name="b"/>
    /// (b empty = the top of the interval). Never ends in '0' (a valid-key invariant).</summary>
    private static string MidFraction(string a, string b)
    {
        if (b.Length > 0)
        {
            var n = 0;
            while (n < b.Length && (n < a.Length ? a[n] : '0') == b[n])
            {
                n++;
            }

            if (n > 0)
            {
                return b[..n] + MidFraction(n < a.Length ? a[n..] : "", b[n..]);
            }
        }

        var digitA = a.Length > 0 ? Digits.IndexOf(a[0], StringComparison.Ordinal) : 0;
        var digitB = b.Length > 0 ? Digits.IndexOf(b[0], StringComparison.Ordinal) : Digits.Length;
        if (digitB - digitA > 1)
        {
            return Digits[(digitA + digitB + 1) / 2].ToString();
        }

        // Consecutive digits: either take b's head alone (valid when b has more digits), or
        // extend a's head with the midpoint of its open tail interval.
        if (b.Length > 1)
        {
            return b[..1];
        }

        return Digits[digitA] + MidFraction(a.Length > 0 ? a[1..] : "", "");
    }
}
