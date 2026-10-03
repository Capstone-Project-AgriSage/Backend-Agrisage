using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;

namespace AgriSage.UnitTests.Domain.Features.Customers;

// Default credit tier of a customer group (database design §35.20).
public class CustomerGroupTests
{
    private static CustomerGroup CreateGroup() => new(Guid.NewGuid(), "REGULAR", "Khách quen");

    [Fact]
    public void A_new_group_suggests_no_credit_tier()
    {
        Assert.Null(CreateGroup().DefaultCreditTierId);
    }

    [Fact]
    public void The_default_credit_tier_can_be_set_changed_and_removed()
    {
        var group = CreateGroup();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        group.SetDefaultCreditTier(first);
        Assert.Equal(first, group.DefaultCreditTierId);

        group.SetDefaultCreditTier(second);
        Assert.Equal(second, group.DefaultCreditTierId);

        group.SetDefaultCreditTier(null);
        Assert.Null(group.DefaultCreditTierId);
    }

    [Fact]
    public void An_empty_tier_id_is_rejected_and_the_current_tier_is_kept()
    {
        var group = CreateGroup();
        var tier = Guid.NewGuid();
        group.SetDefaultCreditTier(tier);

        Assert.Throws<DomainException>(() => group.SetDefaultCreditTier(Guid.Empty));
        Assert.Equal(tier, group.DefaultCreditTierId);
    }
}
