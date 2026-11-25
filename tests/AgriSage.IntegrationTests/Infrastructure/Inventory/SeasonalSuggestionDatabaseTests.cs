using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Inventory;

// F4.7 on the reviewed schema (FLOW_4 "Seasonal product suggestions"): candidates from data, crop protection only
// with an approved link, products not carried, numbers from the replenishment rules, and the reminder. Each test builds
// its own schema inside a rolled-back transaction.
public class SeasonalSuggestionDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The interceptors stamp rows with the real time, so the dates of the test follow today.
    private sealed class Clock : IDateTimeProvider { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    private sealed record World(
        AgriSageDbContext Db, User Actor, Store Store, User Owner, User Sale, DateOnly Today, DateOnly StartsOn,
        DateOnly EndsOn, StoreProduct Fertilizer, StoreProduct LinkedPesticide, StoreProduct UnlinkedPesticide,
        Product NotCarried, StoreProduct Tool, SeasonalRule LastYearRule);

    private static SeasonalSuggestionService Service(World w) => new(w.Db);

    private static SeasonalSuggestionGenerator Generator(World w) =>
        new(new SeasonalSuggestionCandidates(w.Db, new InventoryPlanningData(w.Db, new Clock())),
            Options.Create(new SeasonalSuggestionOptions()), new Clock());

    private static SeasonalSuggestionReminder Reminder(World w, RealDb.Session session) =>
        new(w.Db, Generator(w), new NotificationWriter(w.Db), new AuditTrail(w.Db, session.CurrentUser, new Clock()),
            Options.Create(new SeasonalSuggestionOptions()), new Clock(), NullLogger<SeasonalSuggestionReminder>.Instance);

    // A store with: a fertilizer sold last season and in last season's rule; a pesticide with an approved link for rice;
    // a pesticide sold last season without any link (never suggested); a catalog product with an approved link the
    // store does not carry; a tool with no signal. The season starts in 10 days and lasts 61 days.
    private static async Task<World> BuildAsync(RealDb.Session session)
    {
        var db = session.NewContext();
        var schema = "seasonal_suggestions_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + schema + "; SET LOCAL search_path TO " + schema + ";", Token);
        await db.Database.ExecuteSqlRawAsync(
            db.GetService<IMigrator>().GenerateScript(null, null, MigrationsSqlGenerationOptions.NoTransactions), Token);
        var (actor, store, _) = await session.SeedAsync(db);
        session.CurrentUser.UserId = actor.Id;
        session.CurrentUser.Role = "STORE_OWNER";

        var today = BusinessCalendar.Today(DateTimeOffset.UtcNow);
        var startsOn = today.AddDays(10);
        var endsOn = startsOn.AddDays(60);
        var (from, to) = SeasonalSuggestionRules.ComparedWindow(startsOn, endsOn, today);

        // Owner (SEASONAL_RULES.MANAGE by role) and a Sale member without the delegation.
        var ownerRole = await db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == RoleCode.StoreOwner, Token)
            ?? new Role(RoleCode.StoreOwner, "Store owner");
        var saleRole = await db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == RoleCode.SalesStaff, Token)
            ?? new Role(RoleCode.SalesStaff, "Sales staff");
        if (db.Entry(ownerRole).State == EntityState.Detached) db.Add(ownerRole);
        if (db.Entry(saleRole).State == EntityState.Detached) db.Add(saleRole);
        var manage = await db.Permissions.SingleAsync(p => p.Code == "SEASONAL_RULES.MANAGE", Token);
        if (!await db.RolePermissions.AnyAsync(r => r.RoleId == ownerRole.Id && r.PermissionId == manage.Id, Token))
            db.Add(new RolePermission(ownerRole.Id, manage.Id, true));
        var owner = new User(ownerRole.Id, "Chủ cửa hàng", "hash", $"owner-{Guid.NewGuid():N}@example.test", null);
        var sale = new User(saleRole.Id, "Nhân viên", "hash", $"sale-{Guid.NewGuid():N}@example.test", null);
        db.AddRange(owner, sale, new StoreMember(store.Id, owner.Id), new StoreMember(store.Id, sale.Id));

        var bag = new Unit("BAG", "Bao");
        var bottle = new Unit("BOTTLE", "Chai");
        var fertilizerCategory = new Category("FERT-T", "Phân bón");
        var pesticideCategory = new Category("BVTV-T", "Thuốc BVTV");
        var toolCategory = new Category("TOOL-T", "Dụng cụ");
        db.AddRange(bag, bottle, fertilizerCategory, pesticideCategory, toolCategory);

        Product NewProduct(Category category, string sku, string name, Unit unit)
        {
            var product = new Product(category.Id, sku, name);
            product.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE");
            db.Add(product);
            return product;
        }

        var fertilizer = NewProduct(fertilizerCategory, "NPK-T", "Phân NPK 16-16-8", bag);
        var linkedPesticide = NewProduct(pesticideCategory, "DAO-ON-T", "Thuốc trị đạo ôn", bottle);
        var unlinkedPesticide = NewProduct(pesticideCategory, "SAU-T", "Thuốc trừ sâu cuốn lá", bottle);
        var notCarried = NewProduct(pesticideCategory, "RAY-T", "Thuốc trị rầy nâu", bottle);
        var tool = NewProduct(toolCategory, "BINH-T", "Bình phun", bottle);
        var ingredient = new ActiveIngredient("Tricyclazole");
        db.AddRange(ingredient, new ProductActiveIngredient(linkedPesticide.Id, ingredient.Id),
            new ProductActiveIngredient(unlinkedPesticide.Id, ingredient.Id));

        var blast = new Disease("BLAST_T", "Đạo ôn", cropType: "RICE");
        var hopper = new Disease("HOPPER_T", "Rầy nâu", cropType: "RICE", problemType: AgriSage.Domain.Features.Diagnosis.Enums.ProblemType.Pest);
        db.AddRange(blast, hopper);
        foreach (var (disease, product) in new[] { (blast, linkedPesticide), (hopper, notCarried) })
        {
            var link = new DiseaseProduct(disease.Id, product.Id, RecommendationGroup.Protection, "1 chai/ha",
                registrationSource: "Danh mục thuốc BVTV, trang 12");
            link.Approve(actor.Id, DateTimeOffset.UtcNow);
            db.Add(link);
        }

        StoreProduct Carry(Product product)
        {
            var storeProduct = new StoreProduct(store.Id, product.Id);
            db.Add(storeProduct);
            return storeProduct;
        }

        var fertilizerInStore = Carry(fertilizer);
        var linkedInStore = Carry(linkedPesticide);
        var unlinkedInStore = Carry(unlinkedPesticide);
        var toolInStore = Carry(tool);

        // History: stock in before the window, then 3 sales of 100 bags and one of the unlinked pesticide in it.
        var fertilizerLot = new InventoryLot(fertilizerInStore.Id, "NPK-LOT", expiryDate: today.AddDays(400));
        var pesticideLot = new InventoryLot(unlinkedInStore.Id, "SAU-LOT", expiryDate: today.AddDays(400));
        db.AddRange(fertilizerLot, pesticideLot);
        var number = 0;
        void Move(InventoryLot lot, StockMovementType type, DateOnly day, AgriSage.Domain.Features.Inventory.LotBalanceChange change)
        {
            var at = BusinessCalendar.StartOfDay(day).AddHours(5);
            var movement = new StockMovement(store.Id, $"SS-{++number}", type, at, actor.Id);
            movement.AddItem(lot.Id, change);
            movement.Post(actor.Id, at);
            db.Add(movement);
        }

        Move(fertilizerLot, StockMovementType.StockIn, from.AddDays(-30), fertilizerLot.ReceiveStock(1_000, 10));
        Move(pesticideLot, StockMovementType.StockIn, from.AddDays(-30), pesticideLot.ReceiveStock(200, 10));
        foreach (var offset in new[] { 5, 20, 40 })
            Move(fertilizerLot, StockMovementType.Sale, from.AddDays(offset), fertilizerLot.IssueUnreserved(100));
        Move(pesticideLot, StockMovementType.Sale, from.AddDays(10), pesticideLot.IssueUnreserved(50));

        // Last season's rule of the crop holds the fertilizer (its dates are last year's same window).
        var lastYear = new SeasonalRule(store.Id, "Vụ lúa năm trước", "RICE", null, from, to, 14, 1.2m);
        lastYear.SetItems([(fertilizerInStore.Id, null)], actor.Id, DateTimeOffset.UtcNow);
        db.Add(lastYear);
        await db.SaveChangesAsync(Token);

        return new World(db, actor, store, owner, sale, today, startsOn, endsOn, fertilizerInStore, linkedInStore,
            unlinkedInStore, notCarried, toolInStore, lastYear);
    }

    [RealDbFact]
    public async Task Candidates_come_from_data_and_crop_protection_needs_a_link()
    {
        await using var session = await RealDb.Session.StartAsync();
        var w = await BuildAsync(session);
        await using var _ = w.Db;
        await Reminder(w, session).RemindAsync(Token);
        var service = Service(w);
        var listed = Assert.Single((await service.ListAsync(new(), Token)).Items);
        Assert.Equal(3, listed.ItemCount);
        var run = await service.GetAsync(listed.Id, Token);

        Assert.Equal(3, run.CandidateCount);
        Assert.Equal(w.StartsOn.AddYears(-1), run.ComparedFrom);
        Assert.Equal("Test", run.Region); // the store's province
        Assert.Equal(["Phân NPK 16-16-8", "Thuốc trị đạo ôn", "Thuốc trị rầy nâu"], run.Items.Select(i => i.ProductName));
        Assert.DoesNotContain(run.Items, i => i.ProductId == w.UnlinkedPesticide.ProductId || i.ProductId == w.Tool.ProductId);

        var fertilizer = run.Items[0];
        Assert.True(fertilizer.InStore);
        Assert.Equal("FERTILIZER", fertilizer.Group);
        Assert.Equal("HIGH", fertilizer.Confidence);
        Assert.Equal(300, fertilizer.SoldLastSeasonBase);
        Assert.Equal(700, fertilizer.SellableBase);
        // 300 over 61 observed days (3 sale days) × (7 lead + 14 coverage), rounded up.
        Assert.Equal(104, fertilizer.SuggestedTargetBase);
        Assert.Equal(w.StartsOn.AddDays(-21), fertilizer.OrderByDate);
        Assert.Equal(["SALES", "RULE", "STOCK"], fertilizer.Evidence.Select(e => e.Kind));

        var linked = run.Items[1];
        Assert.Equal("CROP_PROTECTION", linked.Group);
        Assert.Null(linked.SuggestedTargetBase);
        Assert.Contains(linked.Evidence, e => e.Kind == "LINK" && e.Text.Contains("Đạo ôn"));

        var notCarried = run.Items[2];
        Assert.False(notCarried.InStore);
        Assert.Null(notCarried.StoreProductId);
        Assert.Null(notCarried.SoldLastSeasonBase);
        Assert.Null(notCarried.SuggestedTargetBase);
        Assert.Contains("chưa kinh doanh", notCarried.Reason);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid(), Token));
    }

    [RealDbFact]
    public async Task The_reminder_prepares_one_run_per_upcoming_season_and_notifies_holders_once()
    {
        await using var session = await RealDb.Session.StartAsync();
        var w = await BuildAsync(session);
        await using var _ = w.Db;
        var reminder = Reminder(w, session);

        // Last year's rule announces this year's season, 10 days ahead.
        var upcoming = Assert.Single(await reminder.UpcomingAsync(w.Store.Id, w.Today, 30, Token));
        Assert.Equal($"RULE:{w.LastYearRule.Id:N}:{w.StartsOn.Year}", upcoming.Key);
        Assert.Equal((w.StartsOn, "RICE"), (upcoming.StartsOn, upcoming.Crop));

        await reminder.RemindAsync(Token);
        await reminder.RemindAsync(Token); // a second hour: nothing new

        var run = Assert.Single(await w.Db.SeasonalSuggestionRuns.Include(r => r.Items).ToListAsync(Token));
        Assert.Equal(upcoming.Key, run.ScheduleKey);
        Assert.Equal(w.LastYearRule.Id, run.SeasonalRuleId);
        Assert.Equal(3, run.Items.Count);

        // 10 days ahead is inside both marks: the owner gets both notices once; the Sale member without the
        // delegation gets none.
        var notices = await w.Db.Notifications.Where(n => n.NotificationType == SeasonalSuggestionReminder.NotificationType)
            .Select(n => new { n.UserId, n.DeduplicationKey, n.Title }).ToListAsync(Token);
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n => Assert.Equal(w.Owner.Id, n.UserId));
        Assert.Contains(notices, n => n.DeduplicationKey!.EndsWith(":FIRST") && n.Title == "Sắp vào vụ Lúa");
        Assert.Contains(notices, n => n.DeduplicationKey!.EndsWith(":SECOND") && n.Title == "Còn 10 ngày đến vụ Lúa");

        // Once an active rule of the crop covers the season, it is no longer upcoming.
        var rule = new SeasonalRule(w.Store.Id, "Vụ lúa năm nay", "RICE", null, w.StartsOn, w.EndsOn, 14, 1m);
        rule.SetItems([(w.Fertilizer.Id, null)], w.Actor.Id, DateTimeOffset.UtcNow);
        w.Db.Add(rule);
        await w.Db.SaveChangesAsync(Token);
        Assert.Empty(await reminder.UpcomingAsync(w.Store.Id, w.Today, 30, Token));
    }

    [RealDbFact]
    public async Task An_open_crop_season_without_a_rule_is_upcoming_and_wins_over_last_years_rule()
    {
        await using var session = await RealDb.Session.StartAsync();
        var w = await BuildAsync(session);
        await using var _ = w.Db;
        var season = new AgriSage.Domain.Features.Debt.Entities.CropSeason(w.Store.Id, "VU-T", "Vụ lúa Đông Xuân", "RICE",
            w.StartsOn, w.StartsOn.AddDays(90), w.StartsOn.AddDays(120), 15);
        w.Db.Add(season);
        await w.Db.SaveChangesAsync(Token);

        var upcoming = Assert.Single(await Reminder(w, session).UpcomingAsync(w.Store.Id, w.Today, 30, Token));
        Assert.Equal($"SEASON:{season.Id:N}", upcoming.Key);
        Assert.Equal(season.Id, upcoming.CropSeasonId);
        Assert.Equal(w.StartsOn, upcoming.StartsOn);
        Assert.Equal(w.StartsOn.AddDays(89), upcoming.EndsOn); // the day before the harvest starts
    }
}
