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
}
