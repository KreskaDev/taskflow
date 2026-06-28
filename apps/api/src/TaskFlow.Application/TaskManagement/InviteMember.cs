using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Invites an admitted User to a shared project by email at an assignable role (contracts/openapi.yaml
/// <c>inviteProjectMember</c>, research R4, OOS-18). Owner-only manage op (editor/viewer member → 403,
/// non-member → 404). The email is resolved SERVER-SIDE against existing admitted Users; an unknown email,
/// the owner's own email, or an already-existing member each → <b>422</b> with a distinct field message (no
/// new code, no enumeration oracle beyond "not an admitted member" — R4). VERSIONED: stale → 409.
/// </summary>
public sealed record InviteMember
{
    /// <summary>The shared project, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The invitee's email (resolved server-side against admitted Users).</summary>
    public required string Email { get; init; }

    /// <summary>The assignable role (<c>editor</c> or <c>viewer</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The Project optimistic-concurrency token.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Boundary validation for <see cref="InviteMember"/> (R4/R16): the email is present and well-formed, and
/// the role is an assignable stored value (<c>editor</c>/<c>viewer</c> — <c>owner</c> is unrepresentable,
/// R2). The cross-row email resolution (unknown/self/duplicate) is in the handler (needs repository
/// lookups). A violation → 422 <c>validation_failed</c> via the Wolverine FluentValidation pipeline.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-004 CreateProjectValidator).")]
public sealed class InviteMemberValidator : AbstractValidator<InviteMember>
{
    private const int MaxEmailLength = 320;

    public InviteMemberValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email must not be empty.")
            .MaximumLength(MaxEmailLength).WithMessage($"Email must be {MaxEmailLength} characters or fewer.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Role)
            .Must(MembershipRoles.IsAssignable)
            .WithMessage($"Role must be '{MembershipRoles.Editor}' or '{MembershipRoles.Viewer}'.");
    }
}

/// <summary>
/// Handles <see cref="InviteMember"/>: member-readable load (foreign/non-member → 404), owner-role gate
/// (editor/viewer member → 403), shared guard, version guard, then the server-side email resolution (R4)
/// and the insert — all in the per-message transaction.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 handlers).")]
public static class InviteMemberHandler
{
    public static async Task<MemberResponse> Handle(
        InviteMember command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IUserRepository users,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(authorization);

        var project = await MembershipGuards
            .LoadManageableSharedProjectAsync(command.Id, currentUser, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // Resolve the email SERVER-SIDE (R4). Unknown / self / duplicate all collapse to a 422 on `email`
        // with a distinct field message — one shape, no finer enumeration oracle than "not an admitted member".
        var invitee = await users.FindByEmailAsync(command.Email, cancellationToken).ConfigureAwait(false);
        if (invitee is null)
        {
            throw EmailValidation("No admitted user with that email — ask them to sign in once first.");
        }

        if (invitee.Id == project.OwnerId)
        {
            throw EmailValidation("That is already the project owner.");
        }

        var existing = await members.FindAsync(command.Id, invitee.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw EmailValidation("That user is already a member — change their role instead.");
        }

        var utcNow = DateTime.UtcNow;
        members.Add(ProjectMembership.Create(ProjectMembershipId.New(), command.Id, invitee.Id, command.Role, utcNow));
        project.RecordMembershipChange(utcNow);

        try
        {
            await members.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateMembershipException)
        {
            // A concurrent double-invite raced past the FindAsync pre-check — surface the same 422 shape.
            throw EmailValidation("That user is already a member — change their role instead.");
        }

        var role = command.Role == MembershipRoles.Editor ? EffectiveRole.Editor : EffectiveRole.Viewer;
        return MemberResponse.From(invitee.Id.Value, invitee.DisplayName, role);
    }

    // The field key is the wire field name (camelCase "email"), matching the slice-004 handler-thrown
    // validation posture (DeleteProject uses literal "taskDisposition") and the request body's JSON field.
    private static ValidationException EmailValidation(string message) =>
        new([new FluentValidation.Results.ValidationFailure("email", message)]);
}
