using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Content;

public sealed record ArticleRequest(string Title, string Slug, string Content, string? Summary = null, string? ThumbnailUrl = null);
public sealed record ArticleStatusRequest(string Status);
public sealed record ArticleListRequest : PaginationRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
}
public sealed record ArticleResponse(Guid Id, string Title, string Slug, string? Summary, string Content,
    string? ThumbnailUrl, string Status, Guid AuthorId, string AuthorName, DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ContactRequestCreate(string ContactName, string Subject, string Message,
    string? ContactPhone = null, string? ContactEmail = null);
public sealed record ContactRequestUpdate(string Status, string? ResolutionNote = null);
public sealed record ContactRequestList : PaginationRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
}
public sealed record ContactRequestResponse(Guid Id, string RequestNumber, string ContactName,
    string? ContactPhone, string? ContactEmail, string Subject, string Message, string Status,
    DateTimeOffset? ResolvedAt, string? ResolutionNote, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IArticleService
{
    Task<PagedResult<ArticleResponse>> ListAsync(ArticleListRequest request, bool publishedOnly, CancellationToken token);
    Task<ArticleResponse> GetPublishedAsync(string slug, CancellationToken token);
    Task<ArticleResponse> GetAsync(Guid id, CancellationToken token);
    Task<ArticleResponse> CreateAsync(ArticleRequest request, CancellationToken token);
    Task<ArticleResponse> UpdateAsync(Guid id, ArticleRequest request, CancellationToken token);
    Task<ArticleResponse> SetStatusAsync(Guid id, ArticleStatusRequest request, CancellationToken token);
    Task DeleteAsync(Guid id, CancellationToken token);
}
public interface IContactRequestService
{
    Task<ContactRequestResponse> CreateAsync(ContactRequestCreate request, CancellationToken token);
    Task<PagedResult<ContactRequestResponse>> ListAsync(ContactRequestList request, bool mine, CancellationToken token);
    Task<ContactRequestResponse> GetAsync(Guid id, bool mine, CancellationToken token);
    Task<ContactRequestResponse> UpdateAsync(Guid id, ContactRequestUpdate request, CancellationToken token);
}
