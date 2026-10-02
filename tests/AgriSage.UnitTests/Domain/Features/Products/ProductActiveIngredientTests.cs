using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.UnitTests.Domain.Features.Products;

public class ProductActiveIngredientTests
{
    private static ProductActiveIngredient NewLink() => new(Guid.NewGuid(), Guid.NewGuid(), "10%", "first");

    [Fact]
    public void Reinstate_revives_a_deleted_link_with_the_new_values()
    {
        var link = NewLink();
        link.MarkDeleted(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.True(link.IsDeleted);

        link.Reinstate("20%", null);

        Assert.False(link.IsDeleted);
        Assert.Null(link.DeletedAt);
        Assert.Null(link.DeletedBy);
        Assert.Equal("20%", link.Concentration);
        Assert.Null(link.Note);
    }

    [Fact]
    public void Reinstate_is_rejected_for_a_link_that_is_not_deleted()
    {
        var link = NewLink();

        Assert.Throws<DomainException>(() => link.Reinstate("20%", null));
        Assert.Equal("10%", link.Concentration);
    }

    [Fact]
    public void A_revived_link_can_be_deleted_again()
    {
        var link = NewLink();
        link.MarkDeleted(null, DateTimeOffset.UtcNow);
        link.Reinstate(null, null);

        link.MarkDeleted(null, DateTimeOffset.UtcNow);

        Assert.True(link.IsDeleted);
    }
}
