using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;

namespace AgriSage.UnitTests.Domain.Features.Customers;

public class CustomerGroupAssignmentTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CustomerGroupAssignment CreateAssignment() =>
        new(Guid.NewGuid(), Guid.NewGuid(), EffectiveFrom, assignedBy: Guid.NewGuid());

    [Fact]
    public void New_assignment_is_current()
    {
        Assert.True(CreateAssignment().IsCurrent);
    }

    [Fact]
    public void End_closes_the_assignment_and_keeps_it_as_history()
    {
        var assignment = CreateAssignment();

        assignment.End(EffectiveFrom.AddMonths(3));

        Assert.False(assignment.IsCurrent);
        Assert.Equal(EffectiveFrom.AddMonths(3), assignment.EffectiveTo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void End_not_after_start_throws(int hoursFromStart)
    {
        var assignment = CreateAssignment();

        Assert.Throws<DomainException>(() => assignment.End(EffectiveFrom.AddHours(hoursFromStart)));
        Assert.True(assignment.IsCurrent);
    }

    [Fact]
    public void End_twice_throws()
    {
        var assignment = CreateAssignment();
        assignment.End(EffectiveFrom.AddMonths(3));

        Assert.Throws<DomainException>(() => assignment.End(EffectiveFrom.AddMonths(4)));
    }
}
