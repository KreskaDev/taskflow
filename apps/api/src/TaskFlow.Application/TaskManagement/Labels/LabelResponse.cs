using Label = TaskFlow.Domain.TaskManagement.Label;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// The lean per-user label read model (contracts/openapi.yaml <c>LabelResponse</c>, R6). Returned by
/// create/update (single) and list (array). Carries NO <c>ownerId</c> (always the caller) and never the
/// normalized name. The <see cref="Name"/> is untrusted content — output-escaped on render (FR-099).
/// </summary>
public sealed record LabelResponse
{
    /// <summary>The client-generated UUIDv7 identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The label name (≤ 50 chars).</summary>
    public required string Name { get; init; }

    /// <summary>The optional preset color token, or null. Decorative only (FR-044); NOT in <c>required[]</c>.</summary>
    public string? Color { get; init; }

    /// <summary>Projects a <see cref="Label"/> aggregate to its lean wire model.</summary>
    public static LabelResponse From(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new LabelResponse { Id = label.Id.Value, Name = label.Name, Color = label.Color };
    }
}
