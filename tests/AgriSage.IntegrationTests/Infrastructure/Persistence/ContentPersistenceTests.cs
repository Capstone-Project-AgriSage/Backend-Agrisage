using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Content;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public class ContentPersistenceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [CreditDbFact]
    public async Task Article_publication_edit_archive_and_soft_delete_stay_consistent()
    {
        await using var database = await CreditTestDatabase.StartAsync(Token);
        using var scope = database.Scope();
        var service = scope.ServiceProvider.GetRequiredService<IArticleService>();
        var draft = await service.CreateAsync(new("Title", "test-article", "Actual content", ThumbnailUrl: "https://storage.example.test/image.jpg"), Token);
        Assert.Equal("DRAFT", draft.Status);
        Assert.Empty((await service.ListAsync(new(), true, Token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetPublishedAsync(draft.Slug, Token));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(new("Duplicate", draft.Slug, "Body"), Token));
        var published = await service.SetStatusAsync(draft.Id, new("PUBLISHED"), Token);
        Assert.NotNull(published.PublishedAt);
        Assert.Equal("Actual content", (await service.GetPublishedAsync(draft.Slug, Token)).Content);
        await service.UpdateAsync(draft.Id, new("Updated", draft.Slug, "Changed\nContent", ThumbnailUrl: draft.ThumbnailUrl), Token);
        Assert.Equal("Changed\nContent", (await service.GetPublishedAsync(draft.Slug, Token)).Content);
        await service.SetStatusAsync(draft.Id, new("ARCHIVED"), Token);
        Assert.Empty((await service.ListAsync(new(), true, Token)).Items);
        await service.DeleteAsync(draft.Id, Token);
        Assert.Empty((await service.ListAsync(new(), false, Token)).Items);
        var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.NotNull((await db.Articles.IgnoreQueryFilters().SingleAsync(a => a.Id == draft.Id, Token)).DeletedAt);
    }

    [CreditDbFact]
    public async Task Contact_history_is_owned_by_jwt_user_and_shows_staff_reply()
    {
        await using var database = await CreditTestDatabase.StartAsync(Token);
        database.User.UserId = database.FarmerUserId;
        using var scope = database.Scope();
        var service = scope.ServiceProvider.GetRequiredService<IContactRequestService>();
        var created = await service.CreateAsync(new("Farmer", "Help", "Need support", "0912345678"), Token);
        Assert.Equal("OPEN", created.Status);
        Assert.Single((await service.ListAsync(new(), true, Token)).Items);
        var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var ownerId = await db.Users.Where(u => u.FullName == "Owner").Select(u => u.Id).SingleAsync(Token);
        database.User.UserId = ownerId;
        Assert.Empty((await service.ListAsync(new(), true, Token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(created.Id, true, Token));
        var updated = await service.UpdateAsync(created.Id, new("RESOLVED", "Đã hỗ trợ"), Token);
        Assert.NotNull(updated.ResolvedAt);
        database.User.UserId = database.FarmerUserId;
        Assert.Equal("Đã hỗ trợ", (await service.GetAsync(created.Id, true, Token)).ResolutionNote);
        database.User.UserId = null;
        var guest = await service.CreateAsync(new("Guest", "Help", "Guest message", ContactEmail: "guest@example.test"), Token);
        database.User.UserId = database.FarmerUserId;
        Assert.Single((await service.ListAsync(new(), true, Token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(guest.Id, true, Token));
    }
}
