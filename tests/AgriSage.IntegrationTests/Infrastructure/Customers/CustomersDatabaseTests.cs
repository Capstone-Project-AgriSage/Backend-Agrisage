using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgriSage.IntegrationTests.Infrastructure.Customers;

[Collection(RealDb.WalkInPriceListCollection)]
public class CustomersDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Tag() => Guid.NewGuid().ToString("N")[..12];
    private static CreateCustomerRequest Request(string? name = null) => new()
    { FullName = name ?? $"Customer {Tag()}", Email = $"{Tag()}@example.test", Password = "test-only-password" };

    private sealed class Env : IAsyncDisposable
    {
        public Env(RealDb.Session session) : this(session.NewContext()) { }

        public Env(AgriSageDbContext context)
        {
            Context = context;
            User = new RealDb.MutableUser { Role = "ADMIN" };
            var clock = new DateTimeProvider();
            var audit = new AuditTrail(Context, User, clock);
            var writes = new CustomerWrites(Context, User, clock, audit);
            var errors = new NpgsqlErrorClassifier();
            var locks = new RowLockService(Context);
            Customers = new CustomerService(Context, new PasswordHashService(), clock, errors, locks, audit, writes, new PaymentQueries(Context));
            Groups = new CustomerGroupService(Context, clock, errors, locks, new CustomerGroupDefaultSwitcher(Context), writes, audit);
            Credit = new CustomerCreditService(Context, locks, errors, writes, audit, clock);
        }
        public AgriSageDbContext Context { get; }
        public RealDb.MutableUser User { get; }
        public CustomerService Customers { get; }
        public CustomerGroupService Groups { get; }
        public CustomerCreditService Credit { get; }
        public Guid StoreId { get; set; }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var e = new Env(session);
        var role = await e.Context.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.Admin, Token);
        if (role == null) { role = new Role(RoleCode.Admin, "Admin"); e.Context.Roles.Add(role); }
        if (!await e.Context.Roles.AnyAsync(r => r.Code == RoleCode.Farmer, Token))
            e.Context.Roles.Add(new Role(RoleCode.Farmer, "Farmer"));
        var actor = new User(role.Id, "Customer tester", "hash", $"{Tag()}@example.test", null);
        e.Context.Users.Add(actor);
        e.User.UserId = actor.Id;
        e.StoreId = await e.Context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (e.StoreId == Guid.Empty)
        {
            var store = new Store($"T{Tag()}", "Test store", "Address", "Province");
            e.Context.Stores.Add(store);
            e.StoreId = store.Id;
        }
        await e.Context.SaveChangesAsync(Token);
        return e;
    }

    [RealDbFact]
    public async Task Create_update_detail_and_duplicate_normalized_contact()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var phone = $"09{Random.Shared.Next(10000000, 100000000)}";
        var request = Request() with { PhoneNumber = phone, Address = new("Recipient", phone, "Old address", "Can Tho") };
        var c = await e.Customers.CreateAsync(request, Token);
        Assert.Equal("REGISTERED", c.CustomerType);
        Assert.Equal(phone, c.PhoneNumber);
        Assert.NotNull(c.Address);
        var user = await e.Context.Users.SingleAsync(u => u.Id == c.UserId, Token);
        Assert.NotEqual(request.Password, user.PasswordHash);
        Assert.NotEqual(AgriSage.Application.Common.Interfaces.PasswordVerification.Failed, new PasswordHashService().Verify(user.PasswordHash, request.Password));
        await Assert.ThrowsAsync<ConflictException>(() => e.Customers.CreateAsync(Request() with { PhoneNumber = $"+84{phone[1..]}" }, Token));
        var updated = await e.Customers.UpdateAsync(c.Id, new() { FullName = "Updated", Email = request.Email, Notes = "Note" }, Token);
        Assert.Equal("Updated", updated.FullName);
        Assert.Equal("Note", updated.Notes);
        await Assert.ThrowsAsync<ConflictException>(() => e.Customers.CreateAsync(Request() with { Email = request.Email!.ToUpperInvariant() }, Token));
    }

    [RealDbFact]
    public async Task Paging_search_group_status_and_debt_filters_run_in_PostgreSql()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var prefix = Tag();
        var first = await e.Customers.CreateAsync(Request($"{prefix} Alice"), Token);
        await e.Customers.CreateAsync(Request($"{prefix} Bob"), Token);
        var group = await e.Groups.CreateAsync(new(Tag(), "Regular"), Token);
        await e.Customers.AssignGroupAsync(first.Id, new(group.Id), Token);
        var page = await e.Customers.ListAsync(new() { Search = prefix, Page = 2, PageSize = 1 }, Token);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.EndsWith("Bob", page.Items[0].FullName);
        var filtered = await e.Customers.ListAsync(new() { Search = prefix, CustomerGroupId = group.Id, HasDebt = false, Status = "ACTIVE" }, Token);
        Assert.Equal(first.Id, Assert.Single(filtered.Items).Id);
        Assert.Empty((await e.Customers.ListAsync(new() { Search = prefix, CustomerType = "WALK_IN" }, Token)).Items);
        foreach (var sort in new[] { "CREATED_AT", "TOTAL_ORDERS", "CURRENT_DEBT" })
            Assert.Equal(2, (await e.Customers.ListAsync(new() { Search = prefix, SortBy = sort, Descending = true }, Token)).TotalCount);
    }

    [RealDbFact]
    public async Task Group_reassignment_is_atomic_and_moves_tier_without_changing_limit()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var tierA = await e.Credit.CreateTierAsync(new(Tag(), "A", 1000, 10), Token);
        var tierB = await e.Credit.CreateTierAsync(new(Tag(), "B", 2000, 30), Token);
        var groupA = await e.Groups.CreateAsync(new(Tag(), "Group A"), Token);
        var groupB = await e.Groups.CreateAsync(new(Tag(), "Group B"), Token);
        await e.Groups.SetCreditTierAsync(groupA.Id, new(tierA.Id), Token);
        await e.Groups.SetCreditTierAsync(groupB.Id, new(tierB.Id), Token);
        var c = await e.Customers.CreateAsync(Request() with { CustomerGroupId = groupA.Id, AllowCreditPurchase = true }, Token);
        Assert.Equal(10, c.PaymentTermDays);
        var changed = await e.Customers.AssignGroupAsync(c.Id, new(groupB.Id, "Loyal customer"), Token);
        Assert.Equal(1000, changed.CreditLimit);
        Assert.Equal(30, changed.PaymentTermDays);
        var history = await e.Customers.GroupHistoryAsync(c.Id, Token);
        Assert.Equal(2, history.Count);
        Assert.Single(history, h => h.EffectiveTo == null);
        Assert.Single(await e.Credit.HistoryAsync(c.Id, Token));
        await e.Customers.AssignGroupAsync(c.Id, new(groupB.Id), Token);
        Assert.Equal(2, (await e.Customers.GroupHistoryAsync(c.Id, Token)).Count);
    }

    [RealDbFact]
    public async Task Debt_and_credit_summaries_use_ledger_balance_overdue_and_remaining_reservations()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var tier = await e.Credit.CreateTierAsync(new(Tag(), "Credit", 1000, 30), Token);
        var c = await e.Customers.CreateAsync(Request() with { AllowCreditPurchase = true, CreditTierId = tier.Id }, Token);
        var account = await e.Context.DebtAccounts.SingleAsync(a => a.StoreId == e.StoreId && a.FarmerProfileId == c.Id, Token);
        var posting = account.CreateManualAdjustmentEntry($"DE-{Tag()}", 300, BusinessCalendar.Today(DateTimeOffset.UtcNow).AddDays(-1), e.User.UserId!.Value, DateTimeOffset.UtcNow);
        e.Context.DebtEntries.Add(posting.Entry);
        e.Context.DebtTransactions.Add(posting.Transaction);
        var order = new Order(e.StoreId, $"OD-{Tag()}", OrderSource.Counter, CustomerType.Registered, e.User.UserId.Value,
            c.FullName, SettlementType.Credit, FulfillmentType.Pickup, c.Id);
        e.Context.Orders.Add(order);
        var profile = await e.Context.FarmerCreditProfiles.SingleAsync(p => p.FarmerProfileId == c.Id, Token);
        var reservation = new CreditReservation(e.StoreId, profile.Id, order.Id, 400, e.User.UserId.Value, DateTimeOffset.UtcNow);
        reservation.Consume(100);
        reservation.Release(50, e.User.UserId.Value, DateTimeOffset.UtcNow);
        e.Context.CreditReservations.Add(reservation);
        await e.Context.SaveChangesAsync(Token);
        var detail = await e.Customers.GetAsync(c.Id, Token);
        Assert.Equal(300, detail.CurrentDebt);
        Assert.Equal(250, detail.ReservedCredit);
        Assert.Equal(450, detail.AvailableCredit);
        Assert.Equal(300, detail.DebtSummary!.OverdueDebt);
        Assert.Null(detail.DebtSummary.PendingConfirmationDebt);
        Assert.Equal(450, (await e.Credit.GetAsync(c.Id, Token)).AvailableCredit);
        Assert.Single(await e.Credit.ReservationsAsync(c.Id, true, Token));
        Assert.Single((await e.Customers.ListAsync(new() { Search = c.FullName, HasDebt = true }, Token)).Items);
        // Frozen FLOW_3 rule: lower limits are allowed, negative available credit blocks new reservations.
        await e.Credit.LimitAsync(c.Id, new(100, "Reduce limit"), Token);
        Assert.Equal(0, (await e.Customers.GetAsync(c.Id, Token)).AvailableCredit);
    }

    [RealDbFact]
    public async Task Defaults_member_counts_and_group_deactivation_rules()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var c = await e.Customers.CreateAsync(Request(), Token);
        var a = await e.Groups.CreateAsync(new(Tag(), "A"), Token);
        var b = await e.Groups.CreateAsync(new(Tag(), "B"), Token);
        await e.Groups.SetDefaultAsync(a.Id, Token);
        await e.Groups.SetDefaultAsync(b.Id, Token);
        Assert.True((await e.Groups.GetAsync(b.Id, Token)).IsDefault);
        Assert.False((await e.Groups.GetAsync(a.Id, Token)).IsDefault);
        Assert.True((await e.Groups.GetAsync(b.Id, Token)).MemberCount >= 1);
        Assert.Equal(b.Id, (await e.Customers.GetAsync(c.Id, Token)).CustomerGroup!.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Groups.SetActiveAsync(b.Id, false, Token));
        // Also switch to the older id: partial uniqueness must not depend on EF update ordering.
        await e.Groups.SetDefaultAsync(a.Id, Token);
        Assert.True((await e.Groups.GetAsync(a.Id, Token)).IsDefault);
        Assert.False((await e.Groups.GetAsync(b.Id, Token)).IsDefault);
    }

    [RealDbFact]
    public async Task Missing_customer_and_foreign_store_group_are_refused()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        await Assert.ThrowsAsync<NotFoundException>(() => e.Customers.GetAsync(Guid.NewGuid(), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => e.Customers.UpdateAsync(Guid.NewGuid(), new() { FullName = "A", Email = "a@example.test" }, Token));
        var c = await e.Customers.CreateAsync(Request(), Token);
        var otherStore = new Store(Tag(), "Other", "Address", "Province");
        otherStore.ChangeStatus(StoreStatus.Inactive);
        var otherGroup = new CustomerGroup(otherStore.Id, Tag(), "Foreign group");
        e.Context.AddRange(otherStore, otherGroup);
        await e.Context.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<NotFoundException>(() => e.Customers.AssignGroupAsync(c.Id, new(otherGroup.Id), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => e.Groups.GetAsync(otherGroup.Id, Token));
    }

    [RealDbFact]
    public async Task Customer_status_and_credit_status_respect_service_role_checks()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var tier = await e.Credit.CreateTierAsync(new(Tag(), "Credit", 100, 5), Token);
        var c = await e.Customers.CreateAsync(Request() with { AllowCreditPurchase = true, CreditTierId = tier.Id }, Token);
        e.User.Role = "SALES_STAFF";
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Customers.SetStatusAsync(c.Id, new("LOCKED"), Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Customers.UpdateAsync(c.Id, new()
        { FullName = c.FullName, Email = c.Email, AllowCreditPurchase = false, CreditChangeReason = "Disable" }, Token));
        e.User.Role = "ADMIN";
        var disabled = await e.Customers.UpdateAsync(c.Id, new()
        { FullName = c.FullName, Email = c.Email, AllowCreditPurchase = false, CreditChangeReason = "Disable" }, Token);
        Assert.False(disabled.AllowCreditPurchase);
        Assert.Equal(100, disabled.CreditLimit);
    }

    [RealDbFact]
    public async Task History_routes_return_empty_paged_results_and_zero_summaries_for_new_customers()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var c = await e.Customers.CreateAsync(Request(), Token);
        var order = new Order(e.StoreId, $"OD-{Tag()}", OrderSource.Counter, CustomerType.Registered, e.User.UserId!.Value,
            c.FullName, SettlementType.FullPayment, FulfillmentType.Pickup, c.Id);
        e.Context.Orders.Add(order);
        await e.Context.SaveChangesAsync(Token);
        var orders = await e.Customers.OrdersAsync(c.Id, new() { Search = order.OrderNumber, Status = "PENDING_CONFIRMATION", PaymentStatus = "PAID" }, Token);
        Assert.Single(orders.Items); // zero total, no manufactured payment
        Assert.Empty(orders.Items[0].PaymentMethods);
        Assert.Empty((await e.Customers.PaymentsAsync(c.Id, new(), Token)).Items);
        Assert.Equal(0, c.DebtSummary!.TotalPaid);
        Assert.Equal(0, c.TotalPurchaseAmount);
    }

    private static Order AddOrder(Env e, Guid customerId, StoreProduct product, AgriSage.Domain.Features.Products.Entities.ProductPackaging packaging, long quantity)
    {
        var order = new Order(e.StoreId, $"OD-{Tag()}", OrderSource.Counter, CustomerType.Registered,
            e.User.UserId!.Value, "Customer", SettlementType.FullPayment, FulfillmentType.Pickup, customerId);
        order.AddItem(product, packaging, "SKU", "Product", "Bag", quantity, 100);
        e.Context.Orders.Add(order);
        return order;
    }

    private static async Task<(StoreProduct Product, AgriSage.Domain.Features.Products.Entities.ProductPackaging Packaging)> ProductAsync(Env e)
    {
        var category = new Category(Tag(), "Test category");
        var unit = new Unit(Tag(), "Test unit");
        var product = new Product(category.Id, Tag(), "Test product", false, false);
        var packaging = product.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE");
        var storeProduct = new StoreProduct(e.StoreId, product.Id);
        e.Context.AddRange(category, unit, product, storeProduct);
        await e.Context.SaveChangesAsync(Token);
        return (storeProduct, packaging);
    }

    [RealDbFact]
    public async Task Order_totals_history_payment_status_dates_and_store_scope_use_real_transactions()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var c = await e.Customers.CreateAsync(Request(), Token);
        var (product, packaging) = await ProductAsync(e);
        var completed = AddOrder(e, c.Id, product, packaging, 2);
        completed.Confirm(e.User.UserId!.Value, DateTimeOffset.UtcNow);
        completed.RecordFulfillment(completed.Items.Single().Id, 2, e.User.UserId.Value, DateTimeOffset.UtcNow);
        var pending = AddOrder(e, c.Id, product, packaging, 5);
        var cancelled = AddOrder(e, c.Id, product, packaging, 7);
        cancelled.Cancel(e.User.UserId.Value, DateTimeOffset.UtcNow, "Cancelled");
        var other = new Store(Tag(), "Other store", "Address", "Province");
        other.ChangeStatus(StoreStatus.Inactive);
        var foreignOrder = new Order(other.Id, $"OD-{Tag()}", OrderSource.Counter, CustomerType.Registered,
            e.User.UserId.Value, "Customer", SettlementType.FullPayment, FulfillmentType.Pickup, c.Id);
        e.Context.AddRange(other, foreignOrder);
        var cash = new Payment(e.StoreId, $"PM-{Tag()}", PaymentContext.OrderPayment, PaymentMethod.Cash, 50,
            DateTimeOffset.UtcNow, c.Id, e.User.UserId.Value, orderId: completed.Id);
        cash.MarkPaid(PaymentConfirmationSource.Staff, DateTimeOffset.UtcNow, e.User.UserId.Value);
        cash.AllocateToOrder(completed.Id, 50, DateTimeOffset.UtcNow, e.User.UserId.Value);
        e.Context.Payments.Add(cash);
        await e.Context.SaveChangesAsync(Token);
        var detail = await e.Customers.GetAsync(c.Id, Token);
        Assert.Equal(2, detail.TotalOrders);
        Assert.Equal(200, detail.TotalPurchaseAmount);
        var today = BusinessCalendar.Today(DateTimeOffset.UtcNow);
        var history = await e.Customers.OrdersAsync(c.Id, new()
        { Search = completed.OrderNumber, PaymentStatus = "PARTIALLY_PAID", FromDate = today, ToDate = today }, Token);
        var item = Assert.Single(history.Items);
        Assert.Equal("COMPLETED", item.OrderStatus);
        Assert.Equal("CASH", Assert.Single(item.PaymentMethods));
        Assert.Empty((await e.Customers.OrdersAsync(c.Id, new() { Search = foreignOrder.OrderNumber }, Token)).Items);
        Assert.Empty((await e.Customers.OrdersAsync(c.Id, new() { Search = completed.OrderNumber, ToDate = today.AddDays(-1) }, Token)).Items);
        Assert.Single((await e.Customers.OrdersAsync(c.Id, new() { Search = pending.OrderNumber, PaymentStatus = "UNPAID" }, Token)).Items);
        // An unconfirmed provider payment is excluded from both paid amount and methods.
        var payos = new Payment(e.StoreId, $"PM-{Tag()}", PaymentContext.OrderPayment, PaymentMethod.PayOs, 150,
            DateTimeOffset.UtcNow, c.Id, orderId: completed.Id);
        e.Context.Payments.Add(payos);
        await e.Context.SaveChangesAsync(Token);
        Assert.Empty((await e.Customers.OrdersAsync(c.Id, new() { Search = completed.OrderNumber, PaymentStatus = "PAID" }, Token)).Items);
        payos.MarkPaid(PaymentConfirmationSource.PayOsWebhook, DateTimeOffset.UtcNow);
        e.Context.PaymentAllocations.Add(payos.AllocateToOrder(completed.Id, 150, DateTimeOffset.UtcNow));
        await e.Context.SaveChangesAsync(Token);
        var fullyPaid = Assert.Single((await e.Customers.OrdersAsync(c.Id, new() { Search = completed.OrderNumber, PaymentStatus = "PAID" }, Token)).Items);
        Assert.Equal(new[] { "CASH", "PAYOS" }, fullyPaid.PaymentMethods);
    }

    [RealDbFact]
    public async Task Debt_payment_history_reuses_payment_query_and_returns_confirmations_and_allocations()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var tier = await e.Credit.CreateTierAsync(new(Tag(), "Credit", 1000, 30), Token);
        var c = await e.Customers.CreateAsync(Request() with { AllowCreditPurchase = true, CreditTierId = tier.Id }, Token);
        var account = await e.Context.DebtAccounts.SingleAsync(a => a.FarmerProfileId == c.Id && a.StoreId == e.StoreId, Token);
        var posting = account.CreateManualAdjustmentEntry($"DE-{Tag()}", 300, BusinessCalendar.Today(DateTimeOffset.UtcNow).AddDays(-1),
            e.User.UserId!.Value, DateTimeOffset.UtcNow);
        e.Context.DebtEntries.Add(posting.Entry);
        e.Context.DebtTransactions.Add(posting.Transaction);
        var paid = new Payment(e.StoreId, $"PM-{Tag()}", PaymentContext.DebtRepayment, PaymentMethod.Cash, 100,
            DateTimeOffset.UtcNow, c.Id, e.User.UserId.Value);
        paid.MarkPaid(PaymentConfirmationSource.Staff, DateTimeOffset.UtcNow, e.User.UserId.Value);
        var allocation = paid.AllocateToDebtEntry(posting.Entry.Id, 100, DateTimeOffset.UtcNow, e.User.UserId.Value);
        e.Context.Payments.Add(paid);
        e.Context.DebtTransactions.Add(account.ApplyPayment(posting.Entry, allocation, DateTimeOffset.UtcNow, e.User.UserId.Value));
        await e.Context.SaveChangesAsync(Token);
        var summary = await e.Customers.DebtSummaryAsync(c.Id, Token);
        Assert.Equal(200, summary.TotalOutstandingDebt);
        Assert.Equal(200, summary.OverdueDebt);
        Assert.Equal(100, summary.TotalPaid);
        // Route customer/context take precedence over attempts to widen the query.
        var result = await e.Customers.PaymentsAsync(c.Id, new()
        { Status = "PAID", FarmerProfileId = Guid.NewGuid(), PaymentContext = "ORDER_PAYMENT", Search = paid.PaymentNumber }, Token);
        var payment = Assert.Single(result.Items);
        Assert.Equal(e.User.UserId, payment.ConfirmedBy);
        Assert.NotNull(payment.Payment.ConfirmedAt);
        Assert.Equal(posting.Entry.EntryNumber, Assert.Single(payment.DebtEntries).EntryNumber);
        Assert.Equal(100, payment.Payment.Amount);
    }

    [RealDbFact]
    public async Task Invalid_credit_tier_during_creation_saves_no_customer_or_audit()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var request = Request() with { AllowCreditPurchase = true, CreditTierId = Guid.NewGuid() };
        var before = await e.Context.AuditLogs.CountAsync(Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Customers.CreateAsync(request, Token));
        await using var verification = session.NewContext();
        Assert.False(await verification.Users.AnyAsync(u => u.Email == request.Email, Token));
        Assert.Equal(before, await verification.AuditLogs.CountAsync(Token));
    }

    [RealDbFact]
    public async Task Group_price_list_replacement_keeps_history_and_foreign_tier_is_refused()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var e = await PrepareAsync(session);
        var group = await e.Groups.CreateAsync(new(Tag(), "Regular"), Token);
        var first = new AgriSage.Domain.Features.Pricing.Entities.PriceList(e.StoreId, Tag(), "First", DateTimeOffset.UtcNow);
        var second = new AgriSage.Domain.Features.Pricing.Entities.PriceList(e.StoreId, Tag(), "Second", DateTimeOffset.UtcNow);
        e.Context.AddRange(first, second);
        await e.Context.SaveChangesAsync(Token);
        await e.Groups.SetPriceListAsync(group.Id, new(first.Id), Token);
        await e.Groups.SetPriceListAsync(group.Id, new(second.Id), Token);
        var history = await e.Groups.PriceListsAsync(group.Id, Token);
        Assert.Equal(2, history.Count);
        Assert.Equal(second.Id, (await e.Groups.GetAsync(group.Id, Token)).CurrentPriceList!.Id);
        await Assert.ThrowsAsync<ConflictException>(() => e.Groups.DeleteAsync(group.Id, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Groups.SetCreditTierAsync(group.Id, new(Guid.NewGuid()), Token));
        var unused = await e.Groups.CreateAsync(new(Tag(), "Tier history"), Token);
        var tier = await e.Credit.CreateTierAsync(new(Tag(), "Tier", 100, 10), Token);
        await e.Groups.SetCreditTierAsync(unused.Id, new(tier.Id), Token);
        await e.Groups.SetCreditTierAsync(unused.Id, new(null), Token);
        await Assert.ThrowsAsync<ConflictException>(() => e.Groups.DeleteAsync(unused.Id, Token));
    }

    [RealDbFact]
    public async Task Failure_after_save_rolls_back_customer_identity_and_audit_in_an_owned_transaction()
    {
        // No outer transaction: the use case owns this one. The interceptor fails after SQL execution but before
        // commit; every inserted row must disappear. Existing reference/bootstrap data is only read.
        var clock = new DateTimeProvider();
        var actor = new RealDb.MutableUser { Role = "ADMIN" };
        var options = new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(RealDb.ConnectionString())
            .AddInterceptors(new SoftDeleteInterceptor(actor, clock), new AuditableEntityInterceptor(clock),
                new ConcurrencyVersionInterceptor(), new FailAfterSave()).Options;
        var request = Request();
        Guid customerId;
        await using (var e = new Env(new AgriSageDbContext(options)))
        {
            e.User.UserId = await e.Context.Users.AsNoTracking().Where(u => u.Role.Code == RoleCode.Admin && u.Status == UserStatus.Active)
                .Select(u => u.Id).FirstAsync(Token);
            actor.UserId = e.User.UserId;
            await Assert.ThrowsAsync<InjectedSaveFailure>(() => e.Customers.CreateAsync(request, Token));
            customerId = e.Context.FarmerProfiles.Local.Single().Id;
        }
        await using var verification = new AgriSageDbContext(new DbContextOptionsBuilder<AgriSageDbContext>()
            .UseNpgsql(RealDb.ConnectionString()).Options);
        Assert.False(await verification.Users.AnyAsync(u => u.Email == request.Email, Token));
        Assert.False(await verification.FarmerProfiles.AnyAsync(f => f.Id == customerId, Token));
        Assert.False(await verification.AuditLogs.AnyAsync(a => a.EntityId == customerId, Token));
    }

    private sealed class InjectedSaveFailure : Exception;
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InjectedSaveFailure();
    }
}
