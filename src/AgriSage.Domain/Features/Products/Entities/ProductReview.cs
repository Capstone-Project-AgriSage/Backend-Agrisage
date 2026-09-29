using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Products.Entities;

// "Requires purchase + successful fulfillment" and "Farmer is the Order's customer" are checked by Application.
public sealed class ProductReview : SoftDeletableEntity
{
    public const short MinRating = 1;
    public const short MaxRating = 5;

    private ProductReview()
    {
    }

    public ProductReview(
        Guid farmerProfileId,
        Guid storeProductId,
        Guid orderItemId,
        short rating,
        string status,
        string? comment = null)
    {
        FarmerProfileId = farmerProfileId;
        StoreProductId = storeProductId;
        OrderItemId = orderItemId;
        Edit(rating, comment);
        ChangeStatus(status);
    }

    public Guid FarmerProfileId { get; private set; }

    public Guid StoreProductId { get; private set; }

    // FK to order_items (entity introduced in a later task).
    public Guid OrderItemId { get; private set; }

    public short Rating { get; private set; }

    public string? Comment { get; private set; }

    // Allowed values are not defined by the database design yet.
    public string Status { get; private set; } = null!;

    public void Edit(short rating, string? comment)
    {
        if (rating is < MinRating or > MaxRating)
        {
            throw new DomainException($"Rating must be between {MinRating} and {MaxRating}.");
        }

        Rating = rating;
        Comment = comment;
    }

    public void ChangeStatus(string status) => Status = Guard.NotNullOrWhiteSpace(status);
}
