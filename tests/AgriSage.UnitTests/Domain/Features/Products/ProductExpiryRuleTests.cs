using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.UnitTests.Domain.Features.Products;

public class ProductExpiryRuleTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void Expiry_dates_require_lot_tracking(bool lotTracking, bool expiry, bool allowed)
    {
        Product Create() => new(Guid.NewGuid(), "SKU-1", "Product", requiresLotTracking: lotTracking, requiresExpiryDate: expiry);

        if (allowed)
        {
            var product = Create();
            Assert.Equal(lotTracking, product.RequiresLotTracking);
            Assert.Equal(expiry, product.RequiresExpiryDate);
        }
        else
        {
            Assert.Throws<DomainException>(Create);
        }
    }
}
