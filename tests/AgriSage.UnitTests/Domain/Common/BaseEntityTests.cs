using AgriSage.Domain.Common;

namespace AgriSage.UnitTests.Domain.Common;

public class BaseEntityTests
{
    [Fact]
    public void New_entity_gets_non_empty_id()
    {
        var entity = new TestEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public void New_entities_get_distinct_ids()
    {
        var first = new TestEntity();
        var second = new TestEntity();

        Assert.NotEqual(first.Id, second.Id);
    }

    private sealed class TestEntity : BaseEntity;
}
