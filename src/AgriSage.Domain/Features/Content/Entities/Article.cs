using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Content.Enums;

namespace AgriSage.Domain.Features.Content.Entities;

// Agricultural knowledge article. Only the documented invariant is enforced (PUBLISHED → published_at);
// no transition graph is defined. Active-slug uniqueness is enforced by the database.
public sealed class Article : SoftDeletableEntity
{
    private Article()
    {
    }

    public Article(
        string title,
        string slug,
        string content,
        Guid authorId,
        string? summary = null,
        string? thumbnailUrl = null)
    {
        AuthorId = authorId;
        UpdateContent(title, slug, content, summary, thumbnailUrl);
        Status = ArticleStatus.Draft;
    }

    public string Title { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string? Summary { get; private set; }

    public string Content { get; private set; } = null!;

    public string? ThumbnailUrl { get; private set; }

    public ArticleStatus Status { get; private set; }

    public Guid AuthorId { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public void UpdateContent(string title, string slug, string content, string? summary, string? thumbnailUrl)
    {
        Title = Guard.NotNullOrWhiteSpace(title);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        Content = Guard.NotNullOrWhiteSpace(content);
        Summary = summary;
        ThumbnailUrl = thumbnailUrl;
    }

    public void Publish(DateTimeOffset publishedAt)
    {
        Status = ArticleStatus.Published;
        PublishedAt = publishedAt;
    }

    public void Archive() => Status = ArticleStatus.Archived;

    public void MoveToDraft() => Status = ArticleStatus.Draft;
}
