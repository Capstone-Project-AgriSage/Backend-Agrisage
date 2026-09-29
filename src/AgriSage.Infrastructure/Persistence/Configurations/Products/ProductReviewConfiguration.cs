using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 17: product_reviews. Status stays a string (values not defined by the database design).
internal sealed class ProductReviewConfiguration : EntityConfiguration<ProductReview>
{
    protected override string TableName => "product_reviews";

    protected override void ConfigureEntity(EntityTypeBuilder<ProductReview> builder)
    {
        builder.Property(review => review.Rating).HasColumnType("smallint");
        builder.Property(review => review.Comment).IsText();
        builder.Property(review => review.Status).HasMaxLength(20);

        builder.HasReference<ProductReview, FarmerProfile>(review => review.FarmerProfileId);
        builder.HasReference<ProductReview, StoreProduct>(review => review.StoreProductId);
        builder.HasReference<ProductReview, OrderItem>(review => review.OrderItemId);

        builder.HasIndex(review => new { review.FarmerProfileId, review.OrderItemId }).IsUnique();

        builder.HasCheck("rating", "rating BETWEEN 1 AND 5");
    }
}
