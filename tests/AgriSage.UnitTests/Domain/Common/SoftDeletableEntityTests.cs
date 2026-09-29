using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.UnitTests.Domain.Common;

public class SoftDeletableEntityTests
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_entity_is_not_deleted()
    {
        var entity = new TestEntity();

        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);
        Assert.Null(entity.DeletedBy);
    }

    [Fact]
    public void MarkDeleted_records_actor_and_time()
    {
        var entity = new TestEntity();
        var actorId = Guid.NewGuid();

        entity.MarkDeleted(actorId, DeletedAt);

        Assert.True(entity.IsDeleted);
        Assert.Equal(DeletedAt, entity.DeletedAt);
        Assert.Equal(actorId, entity.DeletedBy);
    }

    [Fact]
    public void MarkDeleted_allows_system_action_without_actor()
    {
        var entity = new TestEntity();

        entity.MarkDeleted(deletedBy: null, DeletedAt);

        Assert.True(entity.IsDeleted);
        Assert.Null(entity.DeletedBy);
    }

    [Fact]
    public void MarkDeleted_twice_throws_and_keeps_original_values()
    {
        var entity = new TestEntity();
        var actorId = Guid.NewGuid();
        entity.MarkDeleted(actorId, DeletedAt);

        Assert.Throws<DomainException>(() => entity.MarkDeleted(Guid.NewGuid(), DeletedAt.AddHours(1)));

        Assert.Equal(DeletedAt, entity.DeletedAt);
        Assert.Equal(actorId, entity.DeletedBy);
    }

    private sealed class TestEntity : SoftDeletableEntity;
}
