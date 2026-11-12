using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Content.Entities;
using AgriSage.Domain.Features.Content.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Content;

public sealed class ArticleService(IAgriSageDbContext db, ICurrentUserService actor,
    IDateTimeProvider clock, IDatabaseErrorClassifier databaseErrors) : IArticleService
{
    public async Task<PagedResult<ArticleResponse>> ListAsync(ArticleListRequest request, bool publishedOnly, CancellationToken token)
    {
        var query = db.Articles.AsNoTracking();
        if (publishedOnly) query = query.Where(a => a.Status == ArticleStatus.Published);
        else if (EnumText.TryParse<ArticleStatus>(request.Status, out var status)) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(term) || (a.Summary != null && a.Summary.ToLower().Contains(term)));
        }
        var total = await query.LongCountAsync(token);
        var items = await Project(query.OrderByDescending(a => a.PublishedAt ?? a.CreatedAt).ThenBy(a => a.Id)
            .Skip(request.Skip).Take(request.PageSize)).ToListAsync(token);
        return new(items, request.Page, request.PageSize, total);
    }

    public async Task<ArticleResponse> GetPublishedAsync(string slug, CancellationToken token) =>
        await Project(db.Articles.AsNoTracking().Where(a => a.Slug == slug && a.Status == ArticleStatus.Published))
            .SingleOrDefaultAsync(token) ?? throw new NotFoundException("Article", slug);

    public async Task<ArticleResponse> GetAsync(Guid id, CancellationToken token) =>
        await Project(db.Articles.AsNoTracking().Where(a => a.Id == id)).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Article", id);

    public async Task<ArticleResponse> CreateAsync(ArticleRequest request, CancellationToken token)
    {
        var author = actor.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        await EnsureSlugAsync(request.Slug, null, token);
        var article = new Article(request.Title.Trim(), request.Slug, request.Content.Trim(), author,
            Texts.Clean(request.Summary), Texts.Clean(request.ThumbnailUrl));
        db.Articles.Add(article);
        await SaveAsync(token);
        return await GetAsync(article.Id, token);
    }

    public async Task<ArticleResponse> UpdateAsync(Guid id, ArticleRequest request, CancellationToken token)
    {
        var article = await FindAsync(id, token);
        await EnsureSlugAsync(request.Slug, id, token);
        article.UpdateContent(request.Title.Trim(), request.Slug, request.Content.Trim(), Texts.Clean(request.Summary), Texts.Clean(request.ThumbnailUrl));
        await SaveAsync(token);
        return await GetAsync(id, token);
    }

    public async Task<ArticleResponse> SetStatusAsync(Guid id, ArticleStatusRequest request, CancellationToken token)
    {
        var article = await FindAsync(id, token);
        if (!EnumText.TryParse<ArticleStatus>(request.Status, out var status)) throw new BusinessRuleException("Invalid article status.");
        switch (status)
        {
            case ArticleStatus.Published:
                if (string.IsNullOrWhiteSpace(article.ThumbnailUrl)) throw new BusinessRuleException("Bài viết phải có ảnh đại diện trước khi xuất bản.");
                article.Publish(article.PublishedAt ?? clock.UtcNow);
                break;
            case ArticleStatus.Archived: article.Archive(); break;
            default: article.MoveToDraft(); break;
        }
        await SaveAsync(token);
        return await GetAsync(id, token);
    }

    public async Task DeleteAsync(Guid id, CancellationToken token)
    {
        db.Articles.Remove(await FindAsync(id, token));
        await db.SaveChangesAsync(token);
    }

    private async Task<Article> FindAsync(Guid id, CancellationToken token) =>
        await db.Articles.SingleOrDefaultAsync(a => a.Id == id, token) ?? throw new NotFoundException("Article", id);

    private IQueryable<ArticleResponse> Project(IQueryable<Article> query) =>
        from a in query
        join u in db.Users.AsNoTracking() on a.AuthorId equals u.Id into authors
        from u in authors.DefaultIfEmpty()
        select new ArticleResponse(a.Id, a.Title, a.Slug, a.Summary, a.Content, a.ThumbnailUrl, EnumText.Format(a.Status),
            a.AuthorId, u == null ? "AgriSage" : u.FullName, a.PublishedAt, a.CreatedAt, a.UpdatedAt);

    private async Task EnsureSlugAsync(string slug, Guid? except, CancellationToken token)
    {
        if (await db.Articles.AnyAsync(a => a.Slug == slug && a.Id != except, token))
            throw new ConflictException("Đường dẫn bài viết đã tồn tại.");
    }
    private async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (databaseErrors.IsUniqueViolation(ex))
        { throw new ConflictException("Đường dẫn bài viết đã tồn tại."); }
    }
}
