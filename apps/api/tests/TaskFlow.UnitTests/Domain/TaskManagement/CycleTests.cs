using FluentAssertions;
using Cycle = TaskFlow.Domain.TaskManagement.Cycle;
using CycleId = TaskFlow.Domain.TaskManagement.CycleId;
using CycleStatus = TaskFlow.Domain.TaskManagement.CycleStatus;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// Aggregate invariants for <see cref="Cycle"/> (ENT-03, slice 011 T005): name trimmed-non-empty
/// ≤ 200; <c>start &lt; end</c> is the only cross-field rule; a fresh cycle is planned with
/// version 0; <c>Activate</c> is planned-only and <c>Close</c> active-only (manual transitions);
/// <c>Rename</c>/<c>Reschedule</c> are legal in every status; every mutator bumps <c>Version</c>
/// and stamps <c>UpdatedAt</c>.
/// </summary>
public sealed class CycleTests
{
    private static readonly DateTime CreatedInstant = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MutateInstant = new(2026, 1, 10, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc);

    private static Cycle NewCycle(string name = "Cykl 1")
        => Cycle.Create(CycleId.From(Guid.NewGuid()), name, Start, End, CreatedInstant);

    [Fact]
    public void Create_trims_the_name_and_defaults_to_planned_with_version_zero()
    {
        var id = CycleId.From(Guid.NewGuid());

        var cycle = Cycle.Create(id, "  Cykl 1  ", Start, End, CreatedInstant);

        cycle.Id.Should().Be(id);
        cycle.Name.Should().Be("Cykl 1");
        cycle.StartDate.Should().Be(Start);
        cycle.EndDate.Should().Be(End);
        cycle.Status.Should().Be(CycleStatus.Planned);
        cycle.Version.Should().Be(0, "creation is not a mutation");
        cycle.CreatedAt.Should().Be(CreatedInstant);
        cycle.UpdatedAt.Should().Be(CreatedInstant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => Cycle.Create(CycleId.From(Guid.NewGuid()), name, Start, End, CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_a_name_longer_than_200_chars()
    {
        var act = () => Cycle.Create(CycleId.From(Guid.NewGuid()), new string('x', 201), Start, End, CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_start_not_strictly_before_end()
    {
        var equal = () => Cycle.Create(CycleId.From(Guid.NewGuid()), "X", Start, Start, CreatedInstant);
        var reversed = () => Cycle.Create(CycleId.From(Guid.NewGuid()), "X", End, Start, CreatedInstant);

        equal.Should().Throw<ArgumentException>("start == end violates the only cross-field rule");
        reversed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Activate_moves_planned_to_active_and_bumps_the_version()
    {
        var cycle = NewCycle();

        cycle.Activate(MutateInstant);

        cycle.Status.Should().Be(CycleStatus.Active);
        cycle.Version.Should().Be(1);
        cycle.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void Activate_is_planned_only()
    {
        var active = NewCycle();
        active.Activate(MutateInstant);
        var closed = NewCycle();
        closed.Activate(MutateInstant);
        closed.Close(MutateInstant);

        var fromActive = () => active.Activate(MutateInstant);
        var fromClosed = () => closed.Activate(MutateInstant);

        fromActive.Should().Throw<InvalidOperationException>("active never re-activates");
        fromClosed.Should().Throw<InvalidOperationException>("closed never re-activates");
    }

    [Fact]
    public void Close_moves_active_to_closed_and_bumps_the_version()
    {
        var cycle = NewCycle();
        cycle.Activate(MutateInstant);

        cycle.Close(MutateInstant);

        cycle.Status.Should().Be(CycleStatus.Closed);
        cycle.Version.Should().Be(2);
    }

    [Fact]
    public void Close_is_active_only()
    {
        var planned = NewCycle();
        var closed = NewCycle();
        closed.Activate(MutateInstant);
        closed.Close(MutateInstant);

        var fromPlanned = () => planned.Close(MutateInstant);
        var fromClosed = () => closed.Close(MutateInstant);

        fromPlanned.Should().Throw<InvalidOperationException>("a planned cycle cannot close");
        fromClosed.Should().Throw<InvalidOperationException>("close is not idempotent at the domain level");
    }

    [Fact]
    public void Rename_is_legal_in_every_status_and_trims()
    {
        var cycle = NewCycle();
        cycle.Rename("  Planowany  ", MutateInstant);
        cycle.Name.Should().Be("Planowany");

        cycle.Activate(MutateInstant);
        cycle.Rename("Aktywny", MutateInstant);
        cycle.Name.Should().Be("Aktywny");

        cycle.Close(MutateInstant);
        cycle.Rename("Zamknięty", MutateInstant);
        cycle.Name.Should().Be("Zamknięty");
        cycle.Version.Should().Be(5, "three renames + activate + close each bump once");
    }

    [Fact]
    public void Reschedule_is_legal_in_every_status_and_revalidates_the_order()
    {
        var cycle = NewCycle();
        cycle.Activate(MutateInstant);

        cycle.Reschedule(Start.AddDays(1), End.AddDays(7), MutateInstant);

        cycle.StartDate.Should().Be(Start.AddDays(1));
        cycle.EndDate.Should().Be(End.AddDays(7));

        var reversed = () => cycle.Reschedule(End, Start, MutateInstant);
        reversed.Should().Throw<ArgumentException>("start < end is re-validated on every reschedule");
    }
}
