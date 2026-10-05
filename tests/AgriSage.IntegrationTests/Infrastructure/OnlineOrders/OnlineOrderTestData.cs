using AgriSage.Application.Common;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.OnlineOrders;

// Shared catalog/price setup for the L2 (online order & delivery) real-database tests, modelled on OrdersDatabaseTests.
// Active walk-in lists and default groups already in agrisage-dev are switched off inside the rolled-back transaction,
// so the tests use the walk-in default list they create. Test classes using it join RealDb.WalkInPriceListCollection.
internal sealed class OnlineOrderTestData
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private OnlineOrderTestData(AgriSageDbContext context, RealDb.MutableUser user)
    {
        Context = context;
        User = user;
        var errors = new NpgsqlErrorClassifier();
        var clock = new DateTimeProvider();
        Categories = new CategoryService(context, errors);
        Products = new ProductService(context, errors);
        StoreProducts = new StoreProductService(context, errors);
        PriceLists = new PriceListService(context, clock, errors, new AuditTrail(context, user, clock));
    }

    public AgriSageDbContext Context { get; }

    // The acting user: staff while the catalog is prepared, then a Farmer through ActAsFarmer.
    public RealDb.MutableUser User { get; }

    public CategoryService Categories { get; }

    public ProductService Products { get; }

    public StoreProductService StoreProducts { get; }

    public PriceListService PriceLists { get; }

    public User Staff { get; private set; } = null!;

    public Guid StoreId { get; private set; }

    public Guid WalkInListId { get; private set; }

    private Guid Bottle { get; set; }

    private Guid Box { get; set; }

    private Guid Carton { get; set; }

    public sealed record Priced(Guid ProductId, string Sku, Guid StoreProductId, Guid BottleId, Guid BoxId, Guid CartonId);

    public sealed record Farmer(Guid ProfileId, Guid UserId, string Name, string Phone);

    public static async Task<OnlineOrderTestData> PrepareAsync(RealDb.Session session)
    {
        var data = new OnlineOrderTestData(session.NewContext(), session.CurrentUser);
        var context = data.Context;
        data.User.Role = "STORE_OWNER";

        data.Bottle = await UnitAsync(context, "BOTTLE", "Bottle");
        data.Box = await UnitAsync(context, "BOX", "Box");
        data.Carton = await UnitAsync(context, "CARTON", "Carton");

        data.Staff = new User(await RoleIdAsync(context, RoleCode.StoreOwner), "L2 Tester", "hash", $"{Tag()}@example.test", null);
        context.Users.Add(data.Staff);
        data.User.UserId = data.Staff.Id;

        var storeId = await context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (storeId == Guid.Empty)
        {
            var store = new Store($"T{Tag()}", "Test store", "1 Test Street", "Test");
            context.Stores.Add(store);
            storeId = store.Id;
        }

        data.StoreId = storeId;

        foreach (var list in await context.PriceLists.Where(p => p.StoreId == storeId && p.Status == PriceListStatus.Active).ToListAsync(Token))
        {
            list.Deactivate();
        }

        foreach (var group in await context.CustomerGroups.Where(g => g.StoreId == storeId && g.IsDefault).ToListAsync(Token))
        {
            group.UnsetDefault();
        }

        await context.SaveChangesAsync(Token);

        var walkIn = await data.PriceLists.CreateAsync(
            new PriceListRequest($"PL-{Tag()}", "Bảng giá thử", DateTimeOffset.UtcNow.AddDays(-1), null, true), Token);
        data.WalkInListId = (await data.PriceLists.ActivateAsync(walkIn.Id, Token)).Id;

        return data;
    }

    // Bottle = base + sale (10 000), Box of 6 = purchase + sale (55 000), Carton of 24 = purchase only (not sold, no price).
    public async Task<Priced> NewProductAsync(decimal bottle = 10_000m, decimal box = 55_000m)
    {
        var acting = (User.UserId, User.Role);
        (User.UserId, User.Role) = (Staff.Id, "STORE_OWNER");

        var tag = Tag();
        var category = await Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await Products.CreateAsync(
            new CreateProductRequest(
                $"SKU-{tag}", $"Product {tag}", category.Id,
                [
                    new PackagingRequest(Bottle, 1, true, false, true, "Bottle"),
                    new PackagingRequest(Box, 6, false, true, true, null),
                    new PackagingRequest(Carton, 24, false, true, false, "Carton of 24")
                ]),
            Token);
        var storeProduct = await StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);

        Guid Packaging(Guid unit) => product.Packagings.Single(p => p.UnitId == unit).Id;
        var priced = new Priced(product.Id, product.Sku, storeProduct.Id, Packaging(Bottle), Packaging(Box), Packaging(Carton));
        await PriceLists.UpsertItemsAsync(
            WalkInListId,
            new UpsertPriceListItemsRequest(
            [
                new PriceListItemInput(priced.StoreProductId, priced.BottleId, bottle),
                new PriceListItemInput(priced.StoreProductId, priced.BoxId, box)
            ]),
            Token);

        (User.UserId, User.Role) = acting;
        return priced;
    }

    // A new FARMER user + profile. The session then acts as that Farmer.
    public async Task<Farmer> ActAsNewFarmerAsync()
    {
        var phone = "09" + Random.Shared.Next(10_000_000, 99_999_999);
        var name = $"Nông dân {Tag()}";
        var user = new User(await RoleIdAsync(Context, RoleCode.Farmer), name, "hash", $"{Tag()}@example.test", phone);
        var farmer = new FarmerProfile(user.Id);
        Context.AddRange(user, farmer);
        await Context.SaveChangesAsync(Token);

        ActAs(user.Id, "FARMER");
        return new Farmer(farmer.Id, user.Id, name, phone);
    }

    // A staff user with an ACTIVE membership of the store (e.g. a DELIVERY_STAFF driver).
    public async Task<(Guid UserId, Guid MemberId, string Name)> NewMemberAsync(RoleCode role)
    {
        var name = $"{role} {Tag()}";
        var user = new User(await RoleIdAsync(Context, role), name, "hash", $"{Tag()}@example.test", null);
        var member = new StoreMember(StoreId, user.Id);
        Context.AddRange(user, member);
        await Context.SaveChangesAsync(Token);

        return (user.Id, member.Id, name);
    }

    // A lot with stock on hand (weighted average cost 5 000), outside any goods receipt.
    public async Task<InventoryLot> NewLotAsync(Guid storeProductId, string number, long onHand, DateOnly expiry)
    {
        var lot = new InventoryLot(storeProductId, number, null, expiry);
        lot.ReceiveStock(onHand, 5_000m);
        Context.InventoryLots.Add(lot);
        await Context.SaveChangesAsync(Token);

        return lot;
    }

    public async Task<(long OnHand, long Reserved)> BalanceAsync(Guid lotId)
    {
        var balance = await Context.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == lotId, Token);

        return (balance.QuantityOnHand, balance.QuantityReserved);
    }

    public void ActAs(Guid userId, string role) => (User.UserId, User.Role) = (userId, role);

    public void ActAsStaff() => ActAs(Staff.Id, "STORE_OWNER");

    private static async Task<Guid> UnitAsync(AgriSageDbContext context, string code, string name)
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
}
