using AgriSage.Application.Features.Content;
using AgriSage.Domain.Features.Content.Entities;
using AgriSage.Domain.Features.Content.Enums;

namespace AgriSage.UnitTests.Application.Content;

public class ContentValidationTests
{
    [Fact]
    public void Article_requires_a_thumbnail_and_accepts_public_uploaded_image_url()
    {
        var validator = new ArticleRequestValidator();
        Assert.False(validator.Validate(new ArticleRequest("Title", "slug", "Body")).IsValid);
        Assert.True(validator.Validate(new ArticleRequest("Title", "slug", "Body", ThumbnailUrl: "https://storage.example.test/2026/11/image.jpg")).IsValid);
    }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("/relative")]
    public void Article_rejects_unsafe_image_urls(string url) =>
        Assert.False(new ArticleRequestValidator().Validate(new ArticleRequest("Title", "slug", "Body", ThumbnailUrl: url)).IsValid);

    [Theory]
    [InlineData("Chờ duyệt")]
    [InlineData("PENDING")]
    [InlineData("1")]
    public void Only_documented_article_statuses_are_accepted(string status) =>
        Assert.False(new ArticleStatusRequestValidator().Validate(new ArticleStatusRequest(status)).IsValid);

    [Fact]
    public void Contact_requires_a_valid_reply_channel()
    {
        var validator = new ContactRequestCreateValidator();
        Assert.False(validator.Validate(new ContactRequestCreate("Name", "Help", "Message")).IsValid);
        Assert.False(validator.Validate(new ContactRequestCreate("Name", "Help", "Message", "bad-phone")).IsValid);
        Assert.False(validator.Validate(new ContactRequestCreate("Name", "Help", "Message", ContactEmail: "bad-email")).IsValid);
        Assert.True(validator.Validate(new ContactRequestCreate("Name", "Help", "Message", "0912345678")).IsValid);
        Assert.True(validator.Validate(new ContactRequestCreate("Name", "Help", "Message", ContactEmail: "farmer@example.com")).IsValid);
    }

    [Fact]
    public void Resolved_contact_requires_reply_and_all_notes_respect_database_length()
    {
        var validator = new ContactRequestUpdateValidator();
        Assert.False(validator.Validate(new ContactRequestUpdate("RESOLVED", " ")).IsValid);
        Assert.False(validator.Validate(new ContactRequestUpdate("OPEN", new string('x', 2001))).IsValid);
        Assert.True(validator.Validate(new ContactRequestUpdate("RESOLVED", "Đã hỗ trợ")).IsValid);
        Assert.True(validator.Validate(new ContactRequestUpdate("IN_PROGRESS")).IsValid);
    }

    [Fact]
    public void Reopening_resolved_request_clears_resolution_time()
    {
        var request = new ContactRequest("CR-1", "Name", "Help", "Message");
        request.Resolve(DateTimeOffset.UtcNow, "Reply");
        request.UpdateHandling(ContactRequestStatus.InProgress, null);
        Assert.Null(request.ResolvedAt);
        Assert.Null(request.ResolutionNote);
        Assert.Equal(ContactRequestStatus.InProgress, request.Status);
    }
}
