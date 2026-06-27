using System.Collections.Immutable;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The FROZEN, closed preset color + icon sets for projects (ASM-04, R10). Colors and icons are a
/// constrained, server-known set — never free-form (Principle XII: no styling-injection surface).
/// </summary>
/// <remarks>
/// <para>This is the <b>authoritative</b> set. The <c>CreateProjectValidator</c>/<c>EditProjectValidator</c>
/// (T013/T015) assert membership against <see cref="Colors"/>/<see cref="Icons"/> → an out-of-preset
/// value is a 422 <c>validation_failed</c> (R10/R12). The web mirror lives in
/// <c>apps/web/src/lib/projectPresets.ts</c> and MUST stay byte-consistent with these lists (the
/// <c>projectSchema</c> tests, T023, assert the same tokens verbatim).</para>
/// <para>Tokens are abstract names, not raw hex/SVG — the web layer maps each token to its concrete
/// muted color value and icon glyph, so the wire/storage value is the constrained token.</para>
/// </remarks>
public static class ProjectPresets
{
    /// <summary>The closed set of preset color tokens (ASM-04). Membership is the load-bearing API-tier check (R10).</summary>
    public static readonly ImmutableArray<string> Colors =
    [
        "slate",
        "gray",
        "red",
        "orange",
        "amber",
        "green",
        "teal",
        "blue",
        "indigo",
        "violet",
        "pink",
        "rose",
    ];

    /// <summary>The closed set of preset icon tokens (ASM-04). Membership is the load-bearing API-tier check (R10).</summary>
    public static readonly ImmutableArray<string> Icons =
    [
        "folder",
        "inbox",
        "briefcase",
        "home",
        "star",
        "flag",
        "bookmark",
        "calendar",
        "rocket",
        "target",
        "heart",
        "tag",
    ];

    /// <summary>Whether <paramref name="color"/> is a member of the frozen preset color set.</summary>
    public static bool IsValidColor(string? color) => color is not null && Colors.Contains(color);

    /// <summary>Whether <paramref name="icon"/> is a member of the frozen preset icon set.</summary>
    public static bool IsValidIcon(string? icon) => icon is not null && Icons.Contains(icon);
}
