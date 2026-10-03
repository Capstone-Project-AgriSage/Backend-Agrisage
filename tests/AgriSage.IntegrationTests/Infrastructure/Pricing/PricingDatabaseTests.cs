using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Pricing;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): price lists, price resolution and catalog prices (F1.1), always
// rolled back. Existing active walk-in lists and default groups are switched off inside the transaction first, so
// the results do not depend on data already in agrisage-dev.
public class PricingDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private sealed class Env : IAsyncDisposable
    {
        public Env(AgriSageDbContext context, RealDb.MutableUser user)
        {
            Context = context;
            var errors = new NpgsqlErrorClassifier();
            var clock = new DateTimeProvider();
            Categories = new CategoryService(context, errors);
            Products = new ProductService(context, errors);
            StoreProducts = new StoreProductService(context, errors);
            Catalog = new CatalogService(context, clock);
            PriceLists = new PriceListService(context, clock, errors, new AuditTrail(context, user, clock));
            Resolver = new PriceResolver(context);
        }

        public AgriSageDbContext Context { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public CatalogService Catalog { get; }

        public PriceListService PriceLists { get; }

        public PriceResolver Resolver { get; }

        public User Actor { get; set; } = null!;

        public Guid StoreId { get; set; }

        public Guid Bottle { get; set; }

        public Guid Box { get; set; }

        public Guid Carton { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record Priced(ProductResponse Product, Guid StoreProductId, Guid BottleId, Guid BoxId, Guid CartonId);

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var user = new RealDb.MutableUser { Role = "ADMIN" };
        var env = new Env(session.NewContext(), user);
        var context = env.Context;

        async Task<Guid> UnitAsync(string code, string name)
        {
            var unit = await context.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Code == code, Token);
            if (unit is null)
            {
                unit = new Unit(code, name);
                context.Units.Add(unit);
                await context.SaveChangesAsync(Token);
            }

            return unit.Id;
        }

        env.Bottle = await UnitAsync("BOTTLE", "Bottle");
        env.Box = await UnitAsync("BOX", "Box");
        env.Carton = await UnitAsync("CARTON", "Carton");

        env.Actor = new User(await RoleIdAsync(context, RoleCode.Admin), "Pricing Tester", "hash", $"{Tag()}@example.test", null);
        context.Users.Add(env.Actor);
        user.UserId = env.Actor.Id;

        var storeId = await context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (storeId == Guid.Empty)
        {
            var store = new Store($"T{Tag()}", "Test store", "1 Test Street", "Test");
            context.Stores.Add(store);
            storeId = store.Id;
        }

        env.StoreId = storeId;

        // Start from "no walk-in default list, no default group" (rolled back with the test).
        foreach (var list in await context.PriceLists.Where(p => p.StoreId == storeId && p.Status == PriceListStatus.Active)
                     .ToListAsync(Token))
        {
            list.Deactivate();
        }

        foreach (var group in await context.CustomerGroups.Where(g => g.StoreId == storeId && g.IsDefault).ToListAsync(Token))
        {
            group.UnsetDefault();
        }

        await context.SaveChangesAsync(Token);
        return env;
    }

    private static async Task<Guid> RoleIdAsync(AgriSageDbContext context, RoleCode code)
    {
        var role = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == code, Token);
        if (role is null)
        {
            role = new Role(code, code.ToString());
            context.Roles.Add(role);
            await context.SaveChangesAsync(Token);
        }

        return role.Id;
    }

    // Bottle = base + sale, Box of 6 = purchase + sale, Carton of 24 = purchase only.
    private static async Task<Priced> NewProductAsync(Env env)
    {
        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await env.Products.CreateAsync(
            new CreateProductRequest(
                $"SKU-{tag}", $"Product {tag}", category.Id,
                [
                    new PackagingRequest(env.Bottle, 1, true, false, true, "Bottle"),
                    new PackagingRequest(env.Box, 6, false, true, true, "Box of 6"),
                    new PackagingRequest(env.Carton, 24, false, true, false, "Carton of 24")
                ]),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);

        Guid Packaging(Guid unit) => product.Packagings.Single(p => p.UnitId == unit).Id;
        return new Priced(product, storeProduct.Id, Packaging(env.Bottle), Packaging(env.Box), Packaging(env.Carton));
    }

    private static async Task<PriceListResponse> NewListAsync(
        Env env, bool walkIn = false, bool activate = true, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var list = await env.PriceLists.CreateAsync(
            new PriceListRequest($"PL-{Tag()}", "Bảng giá thử", from ?? Now.AddDays(-1), to, walkIn), Token);

        return activate ? await env.PriceLists.ActivateAsync(list.Id, Token) : list;
    }

    private static async Task<Guid> NewFarmerAsync(Env env)
    {
        var user = new User(await RoleIdAsync(env.Context, RoleCode.Farmer), "Nông dân thử", "hash", $"{Tag()}@example.test", null);
        var farmer = new FarmerProfile(user.Id);
        env.Context.AddRange(user, farmer);
        await env.Context.SaveChangesAsync(Token);
        return farmer.Id;
    }

    private static async Task<CustomerGroup> NewGroupAsync(Env env, bool isDefault = false)
    {
        var group = new CustomerGroup(env.StoreId, $"G{Tag()}", "Khách quen");
        if (isDefault)
        {
            group.SetAsDefault();
        }

        env.Context.CustomerGroups.Add(group);
        await env.Context.SaveChangesAsync(Token);
        return group;
    }

    private static async Task<CustomerGroupPriceList> LinkAsync(Env env, CustomerGroup group, Guid priceListId, DateTimeOffset from)
    {
        var link = new CustomerGroupPriceList(group.Id, priceListId, from, env.Actor.Id);
        env.Context.CustomerGroupPriceLists.Add(link);
        await env.Context.SaveChangesAsync(Token);
        return link;
    }

    // ----- Price lists -----

    [RealDbFact]
    public async Task Price_lists_have_unique_codes_and_can_be_updated_listed_and_found()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var code = $"Retail-{Tag()}";

        var created = await env.PriceLists.CreateAsync(new PriceListRequest($"  {code} ", "Giá lẻ", Now, Description: " Mùa vụ "), Token);
        await Assert.ThrowsAsync<ConflictException>(() =>
            env.PriceLists.CreateAsync(new PriceListRequest(code.ToUpperInvariant(), "Khác", Now), Token));
        var updated = await env.PriceLists.UpdateAsync(
            created.Id, new UpdatePriceListRequest("Giá lẻ 2026", Now.AddDays(-2), Now.AddMonths(6)), Token);

        Assert.Equal((code, "DRAFT", "Mùa vụ"), (created.Code, created.Status, created.Description));
        Assert.Equal("Giá lẻ 2026", updated.Name);
        Assert.NotNull(updated.EffectiveTo);
        Assert.Equal(0, updated.ItemCount);
        var found = await env.PriceLists.ListAsync(new PriceListListRequest { Search = code.ToLowerInvariant(), Status = "draft" }, Token);
        Assert.Equal(created.Id, Assert.Single(found.Items).Id);
    }

    [RealDbFact]
    public async Task Only_one_walk_in_default_list_can_be_active()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);

        var first = await NewListAsync(env, walkIn: true);
        var second = await NewListAsync(env, walkIn: true, activate: false);
        var plain = await NewListAsync(env);

        Assert.Equal("ACTIVE", first.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.PriceLists.ActivateAsync(second.Id, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            env.PriceLists.UpdateAsync(plain.Id, new UpdatePriceListRequest(plain.Name, plain.EffectiveFrom, IsWalkInDefault: true), Token));

        await env.PriceLists.DeactivateAsync(first.Id, Token);
        Assert.Equal("ACTIVE", (await env.PriceLists.ActivateAsync(second.Id, Token)).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.PriceLists.ActivateAsync(first.Id, Token));
    }

    [RealDbFact]
    public async Task Items_are_upserted_removed_and_reinstated_and_every_change_is_audited()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var other = await NewProductAsync(env);
        var list = await NewListAsync(env, activate: false);
        PriceListItemInput Line(Guid packaging, decimal price) => new(priced.StoreProductId, packaging, price);

        var first = await env.PriceLists.UpsertItemsAsync(
            list.Id, new UpsertPriceListItemsRequest([Line(priced.BottleId, 10_000m), Line(priced.BoxId, 55_000m)]), Token);
        var second = await env.PriceLists.UpsertItemsAsync(
            list.Id, new UpsertPriceListItemsRequest([Line(priced.BottleId, 10_000m), Line(priced.BoxId, 56_000m)]), Token);
        var items = await env.PriceLists.ListItemsAsync(list.Id, new PriceListItemListRequest(), Token);
        var box = items.Items.Single(i => i.ProductPackagingId == priced.BoxId);
        await env.PriceLists.DeleteItemAsync(list.Id, box.Id, Token);
        var afterDelete = await env.PriceLists.ListItemsAsync(list.Id, new PriceListItemListRequest { Search = priced.Product.Sku }, Token);
        var third = await env.PriceLists.UpsertItemsAsync(list.Id, new UpsertPriceListItemsRequest([Line(priced.BoxId, 57_000m)]), Token);

        Assert.Equal((2, 0), (first.Created, first.Updated));
        Assert.Equal((0, 1), (second.Created, second.Updated));
        Assert.Equal(("Box of 6", priced.Product.Sku, 56_000m), (box.PackagingName, box.Sku, box.SellingPrice));
        Assert.Equal(priced.BottleId, Assert.Single(afterDelete.Items).ProductPackagingId);
        Assert.Equal((1, 0), (third.Created, third.Updated));
        var rows = await env.Context.PriceListItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.PriceListId == list.Id).ToListAsync(Token);
        Assert.Equal(2, rows.Count);
        var revived = rows.Single(i => i.Id == box.Id);
        Assert.Equal((false, 57_000m), (revived.IsDeleted, revived.SellingPrice));
        Assert.Equal(2, (await env.PriceLists.GetAsync(list.Id, Token)).ItemCount);

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.PriceLists.UpsertItemsAsync(
            list.Id,
            new UpsertPriceListItemsRequest([Line(priced.CartonId, 200_000m), Line(other.BoxId, 1m), Line(priced.BottleId, 9_000m)]),
            Token));
        Assert.Equal(["items[0]", "items[1]"], refused.Errors!.Keys.Order());
        Assert.Equal(10_000m, (await env.PriceLists.ListItemsAsync(list.Id, new PriceListItemListRequest(), Token)).Items
            .Single(i => i.ProductPackagingId == priced.BottleId).SellingPrice);

        var audit = await env.Context.AuditLogs.AsNoTracking().Where(a => a.EntityId == list.Id).ToListAsync(Token);
        Assert.Equal(3, audit.Count(a => a.Action == "PRICE_LIST_ITEMS_CHANGED"));
        Assert.Single(audit, a => a.Action == "PRICE_LIST_ITEM_REMOVED");
        Assert.All(audit, a => Assert.Equal(env.Actor.Id, a.ActorUserId));
    }

    [RealDbFact]
    public async Task Only_draft_lists_never_linked_or_used_can_be_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var active = await NewListAsync(env);
        var linked = await NewListAsync(env, activate: false);
        var unused = await NewListAsync(env, activate: false);
        var group = await NewGroupAsync(env);
        var link = await LinkAsync(env, group, linked.Id, Now);
        link.End(Now.AddMinutes(1));
        await env.Context.SaveChangesAsync(Token);

        await Assert.ThrowsAsync<ConflictException>(() => env.PriceLists.DeleteAsync(active.Id, Token));
        await Assert.ThrowsAsync<ConflictException>(() => env.PriceLists.DeleteAsync(linked.Id, Token));
        await env.PriceLists.DeleteAsync(unused.Id, Token);

        await Assert.ThrowsAsync<NotFoundException>(() => env.PriceLists.GetAsync(unused.Id, Token));
        Assert.Contains(group.Id, (await env.PriceLists.GetAsync(linked.Id, Token)).Groups.Select(g => g.Id));
    }

    // ----- Price resolution (FLOW_1 §3) -----

    [RealDbFact]
    public async Task Prices_resolve_from_the_group_list_then_the_walk_in_default_then_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        var farmer = await NewFarmerAsync(env);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Resolver.GetContextAsync(null, Now, Token));

        var walkIn = await NewListAsync(env, walkIn: true);
        Assert.Equal(new PriceContext(null, walkIn.Id), await env.Resolver.GetContextAsync(null, Now, Token));
        Assert.Equal(new PriceContext(null, walkIn.Id), await env.Resolver.GetContextAsync(farmer, Now, Token));

        // Current assignment → the group's linked ACTIVE list.
        var group = await NewGroupAsync(env);
        var groupList = await NewListAsync(env);
        var link = await LinkAsync(env, group, groupList.Id, Now.AddMinutes(-5));
        env.Context.CustomerGroupAssignments.Add(new CustomerGroupAssignment(farmer, group.Id, Now.AddMinutes(-5), env.Actor.Id));
        await env.Context.SaveChangesAsync(Token);
        Assert.Equal(new PriceContext(group.Id, groupList.Id), await env.Resolver.GetContextAsync(farmer, Now, Token));

        // An INACTIVE list, a link that starts later or a list whose validity has not started → walk-in default.
        await env.PriceLists.DeactivateAsync(groupList.Id, Token);
        Assert.Equal(new PriceContext(group.Id, walkIn.Id), await env.Resolver.GetContextAsync(farmer, Now, Token));
        await env.PriceLists.ActivateAsync(groupList.Id, Token);
        link.End(Now.AddMinutes(-1));
        await LinkAsync(env, group, groupList.Id, Now.AddDays(1));
        Assert.Equal(new PriceContext(group.Id, walkIn.Id), await env.Resolver.GetContextAsync(farmer, Now, Token));
        Assert.Equal(new PriceContext(group.Id, groupList.Id), await env.Resolver.GetContextAsync(farmer, Now.AddDays(2), Token));

        // Before the walk-in default list's validity starts, nothing applies to a walk-in customer.
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Resolver.GetContextAsync(null, Now.AddDays(-2), Token));

        // No assignment → the store's default group and its list.
        var otherFarmer = await NewFarmerAsync(env);
        var defaultGroup = await NewGroupAsync(env, isDefault: true);
        var defaultList = await NewListAsync(env);
        await LinkAsync(env, defaultGroup, defaultList.Id, Now.AddMinutes(-5));
        Assert.Equal(new PriceContext(defaultGroup.Id, defaultList.Id), await env.Resolver.GetContextAsync(otherFarmer, Now, Token));
        Assert.Equal(new PriceContext(null, walkIn.Id), await env.Resolver.GetContextAsync(null, Now, Token));

        // A missing pair has no price.
        await env.PriceLists.UpsertItemsAsync(
            walkIn.Id, new UpsertPriceListItemsRequest([new PriceListItemInput(priced.StoreProductId, priced.BoxId, 55_000m)]), Token);
        var prices = await env.Resolver.GetPricesAsync(
            walkIn.Id,
            [new PriceLine(priced.StoreProductId, priced.BoxId), new PriceLine(priced.StoreProductId, priced.BottleId)],
            Token);
        Assert.Equal(55_000m, Assert.Single(prices).Value);
    }

    [RealDbFact]
    public async Task The_public_catalog_shows_walk_in_default_prices()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var priced = await NewProductAsync(env);
        async Task<PublicProductResponse> DetailAsync() => await env.Catalog.GetProductAsync(priced.StoreProductId, Token);
        async Task<PublicProductListItem> RowAsync() => Assert.Single(
            (await env.Catalog.GetProductsAsync(new CatalogProductListRequest { Search = priced.Product.Sku }, Token)).Items);

        Assert.All((await DetailAsync()).Packagings, p => Assert.Null(p.Price));
        Assert.Null((await RowAsync()).FromPrice);

        var walkIn = await NewListAsync(env, walkIn: true);
        await env.PriceLists.UpsertItemsAsync(
            walkIn.Id, new UpsertPriceListItemsRequest([new PriceListItemInput(priced.StoreProductId, priced.BoxId, 55_000m)]), Token);
        var detail = await DetailAsync();
        Assert.Equal(55_000m, detail.Packagings.Single(p => p.Id == priced.BoxId).Price);
        Assert.Null(detail.Packagings.Single(p => p.Id == priced.BottleId).Price);
        Assert.Equal(55_000m, (await RowAsync()).FromPrice);

        await env.PriceLists.UpsertItemsAsync(
            walkIn.Id, new UpsertPriceListItemsRequest([new PriceListItemInput(priced.StoreProductId, priced.BottleId, 10_000m)]), Token);
        Assert.Equal(10_000m, (await RowAsync()).FromPrice);

        await env.PriceLists.DeactivateAsync(walkIn.Id, Token);
        Assert.Null((await RowAsync()).FromPrice);
    }
}
