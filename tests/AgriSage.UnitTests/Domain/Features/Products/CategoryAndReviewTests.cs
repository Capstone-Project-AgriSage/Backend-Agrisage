using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.UnitTests.Domain.Features.Products;

public class CategoryAndReviewTests
{
    [Fact]
    public void Category_cannot_be_its_own_parent()
    {
        var category = new Category("PESTICIDE", "Pesticides");

        Assert.Throws<DomainException>(() => category.MoveTo(category.Id));
        Assert.Null(category.ParentId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Review_rating_outside_1_to_5_throws(short rating)
    {
        Assert.Throws<DomainException>(() =>
            new ProductReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), rating, "VISIBLE"));
    }

    [Fact]
    public void Invalid_review_edit_keeps_previous_rating_and_comment()
    {
        var review = new ProductReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, "VISIBLE", "Good");

        Assert.Throws<DomainException>(() => review.Edit(9, "Changed"));

        Assert.Equal(4, review.Rating);
        Assert.Equal("Good", review.Comment);
    }
}
