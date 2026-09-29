using AgriSage.Domain.Features.Content.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Content;

// Table 64: articles.
internal sealed class ArticleConfiguration : EntityConfiguration<Article>
{
    protected override string TableName => "articles";

    protected override void ConfigureEntity(EntityTypeBuilder<Article> builder)
    {
        builder.Property(article => article.Title).HasMaxLength(255);
        builder.Property(article => article.Slug).HasMaxLength(255);
        builder.Property(article => article.Summary).HasMaxLength(1000);
        builder.Property(article => article.Content).IsText();
        builder.Property(article => article.ThumbnailUrl).HasMaxLength(1000);
        builder.Property(article => article.Status).HasMaxLength(20);

        builder.HasUserReference(article => article.AuthorId);

        builder.HasIndex(article => article.Slug).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(article => new { article.Status, article.PublishedAt }).IsDescending(false, true);

        builder.HasCheck("published_at", "status <> 'PUBLISHED' OR published_at IS NOT NULL");
    }
}
