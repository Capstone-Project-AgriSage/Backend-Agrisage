using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Customers;

public sealed class CustomerService(IAgriSageDbContext context, IPasswordHashService passwords,
    IDateTimeProvider clock, IDatabaseErrorClassifier databaseErrors, IRowLockService locks,
    AuditTrail audit, CustomerWrites writes, PaymentQueries paymentQueries, CustomerAddresses addresses) : ICustomerService
{
    // Intermediate SQL projection: format API enums only after paging/materialization.
    private sealed class Row
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string FullName { get; init; } = "";
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public UserStatus Status { get; init; }
        public string? Notes { get; init; }
        public Guid? GroupId { get; init; }
        public long Orders { get; init; }
        public decimal Purchases { get; init; }
        public decimal Debt { get; init; }
        public decimal Limit { get; init; }
        public bool CreditEnabled { get; init; }
        public decimal Reserved { get; init; }
        public int? Term { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public async Task<PagedResult<CustomerResponse>> ListAsync(CustomerListRequest request, CancellationToken token)
    {
        writes.Actor();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var query = await RowsAsync(storeId, token);
        if (EnumText.TryParse<CustomerType>(request.CustomerType, out var type) && type == CustomerType.WalkIn)
        {
            query = query.Where(r => false); // WALK_IN is not a persisted customer identity.
        }
        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            ContactNormalizer.TryNormalizePhone(search, out var normalizedPhone);
            query = query.Where(r => r.FullName.ToLower().Contains(term)
                || (r.Phone != null && (r.Phone.Contains(term) || r.Phone == normalizedPhone)));
        }
        if (request.CustomerGroupId is { } group)
        {
            query = query.Where(r => r.GroupId == group);
        }
        if (EnumText.TryParse<UserStatus>(request.Status, out var status))
        {
            query = query.Where(r => r.Status == status);
        }
        if (request.HasDebt is { } debt)
        {
            query = debt ? query.Where(r => r.Debt > 0) : query.Where(r => r.Debt == 0);
        }
        var total = await query.LongCountAsync(token);
        var sorted = (request.SortBy.ToUpperInvariant(), request.Descending) switch
        {
            ("CREATED_AT", true) => query.OrderByDescending(r => r.CreatedAt),
            ("CREATED_AT", false) => query.OrderBy(r => r.CreatedAt),
            ("TOTAL_ORDERS", true) => query.OrderByDescending(r => r.Orders),
            ("TOTAL_ORDERS", false) => query.OrderBy(r => r.Orders),
            ("CURRENT_DEBT", true) => query.OrderByDescending(r => r.Debt),
            ("CURRENT_DEBT", false) => query.OrderBy(r => r.Debt),
            (_, true) => query.OrderByDescending(r => r.FullName),
            _ => query.OrderBy(r => r.FullName)
        };
        var rows = await sorted.ThenBy(r => r.Id).Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        var groups = await GroupReferencesAsync(rows, storeId, token);
        return new PagedResult<CustomerResponse>(rows.Select(r => Map(r, groups)).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken token)
    {
        writes.Actor();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var row = await (await RowsAsync(storeId, token)).FirstOrDefaultAsync(r => r.Id == id, token)
            ?? throw new NotFoundException("Customer", id);
        var groups = await GroupReferencesAsync([row], storeId, token);
        var address = await context.UserAddresses.AsNoTracking().Where(a => a.UserId == row.UserId && a.IsDefault)
            .Select(a => new CustomerAddressRequest(a.RecipientName, a.RecipientPhone, a.AddressLine, a.Province, a.Ward, a.District))
            .FirstOrDefaultAsync(token);
        return Map(row, groups) with { Address = address, DebtSummary = await SummaryAsync(id, storeId, row, token) };
    }

    public async Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken token)
    {
        writes.Actor();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(storeId, token);
        var (phone, email) = Contact(request);
        await EnsureFreeContactAsync(phone, email, null, token);
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.Farmer && r.IsActive, token)
            ?? throw new BusinessRuleException("The Farmer role is not configured.");
        var user = new User(role.Id, request.FullName.Trim(), passwords.Hash(request.Password), email, phone);
        var farmer = new FarmerProfile(user.Id, notes: Texts.Clean(request.Notes));
        context.Users.Add(user);
        context.FarmerProfiles.Add(farmer);
        await ApplyDetailsAsync(farmer, storeId, request, token);
        audit.Record("CUSTOMER_CREATED", "CUSTOMER", farmer.Id, storeId, newValues: Snapshot(farmer));
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(farmer.Id, token);
    }

    public async Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken token)
    {
        writes.Actor();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(storeId, token);
        await locks.LockFarmerProfileAsync(id, token);
        var farmer = await writes.FindAsync(id, token);
        var before = Snapshot(farmer);
        var (phone, email) = Contact(request);
        await EnsureFreeContactAsync(phone, email, farmer.UserId, token);
        farmer.User.UpdateProfile(request.FullName.Trim(), farmer.User.AvatarUrl);
        farmer.User.UpdateContact(email, phone);
        farmer.UpdateDetails(farmer.DateOfBirth, farmer.Gender, Texts.Clean(request.Notes));
        await ApplyDetailsAsync(farmer, storeId, request, token);
        audit.Record("CUSTOMER_UPDATED", "CUSTOMER", id, storeId, before, Snapshot(farmer));
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(id, token);
    }

    public async Task<CustomerResponse> SetStatusAsync(Guid id, CustomerStatusRequest request, CancellationToken token)
    {
        writes.Actor(manage: true);
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(storeId, token);
        await locks.LockFarmerProfileAsync(id, token);
        var farmer = await writes.FindAsync(id, token);
        var before = new { Status = EnumText.Format(farmer.User.Status) };
        if (!EnumText.TryParse<UserStatus>(request.Status, out var status))
        {
            throw new BusinessRuleException("Invalid customer status.");
        }
        farmer.User.ChangeStatus(status);
        audit.Record("CUSTOMER_STATUS_CHANGED", "CUSTOMER", id, storeId, before, new { Status = EnumText.Format(status) });
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(id, token);
    }

    public async Task<CustomerResponse> AssignGroupAsync(Guid id, AssignCustomerGroupRequest request, CancellationToken token)
    {
        writes.Actor();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(storeId, token);
        await locks.LockFarmerProfileAsync(id, token);
        await writes.FindAsync(id, token);
        await writes.AssignAsync(id, storeId, request.CustomerGroupId, request.Reason, token);
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(id, token);
    }

    public async Task<IReadOnlyList<GroupAssignmentResponse>> GroupHistoryAsync(Guid id, CancellationToken token)
    {
        writes.Actor();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await RequireCustomerAsync(id, token);
        return await context.CustomerGroupAssignments.AsNoTracking()
            .Where(a => a.FarmerProfileId == id && a.CustomerGroup.StoreId == storeId)
            .OrderByDescending(a => a.EffectiveFrom).ThenBy(a => a.Id)
            .Select(a => new GroupAssignmentResponse(a.Id,
                new CustomerReference(a.CustomerGroupId, a.CustomerGroup.Code, a.CustomerGroup.Name),
                a.EffectiveFrom, a.EffectiveTo, a.AssignedBy, a.Reason)).ToListAsync(token);
    }

    public async Task<CustomerDebtSummaryResponse> DebtSummaryAsync(Guid id, CancellationToken token) =>
        (await GetAsync(id, token)).DebtSummary!;

    public async Task<PagedResult<CustomerOrderResponse>> OrdersAsync(Guid id, CustomerOrderListRequest request, CancellationToken token)
    {
        writes.Actor();
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await RequireCustomerAsync(id, token);
        var orders = context.Orders.AsNoTracking().Where(o => o.StoreId == storeId && o.FarmerProfileId == id);
        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            orders = orders.Where(o => o.OrderNumber.ToLower().Contains(term));
        }
        if (EnumText.TryParse<OrderStatus>(request.Status, out var status))
        {
            orders = orders.Where(o => o.Status == status);
        }
        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            orders = orders.Where(o => o.CreatedAt >= start);
        }
        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            orders = orders.Where(o => o.CreatedAt < end);
        }
        // This is the existing prepayment ledger's ORDER/ACTIVE/PAID allocation definition, not gross payment amounts.
        var paidAllocations = from a in context.PaymentAllocations.AsNoTracking()
                              join p in context.Payments.AsNoTracking() on a.PaymentId equals p.Id
                              where a.AllocationType == PaymentAllocationType.Order && a.Status == PaymentAllocationStatus.Active
                                  && p.StoreId == storeId && p.Status == PaymentStatus.Paid
                              select new { a.OrderId, a.AllocatedAmount, p.PaymentMethod };
        var rows = orders.Select(o => new
        {
            o.Id, o.OrderNumber, o.CreatedAt, o.TotalAmount, o.Status,
            Paid = paidAllocations.Where(a => a.OrderId == o.Id).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m
        });
        var filter = request.PaymentStatus?.ToUpperInvariant();
        if (filter == "PAID") rows = rows.Where(o => o.Paid >= o.TotalAmount);
        if (filter == "UNPAID") rows = rows.Where(o => o.Paid == 0 && o.TotalAmount > 0);
        if (filter == "PARTIALLY_PAID") rows = rows.Where(o => o.Paid > 0 && o.Paid < o.TotalAmount);
        var total = await rows.LongCountAsync(token);
        var page = await rows.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        var ids = page.Select(o => o.Id).ToList();
        var methods = await paidAllocations.Where(a => a.OrderId != null && ids.Contains(a.OrderId.Value))
            .Select(a => new { Id = a.OrderId!.Value, a.PaymentMethod }).Distinct().ToListAsync(token);
        var items = page.Select(o => new CustomerOrderResponse(o.Id, o.OrderNumber, o.CreatedAt, o.TotalAmount,
            methods.Where(m => m.Id == o.Id).Select(m => PaymentText.Format(m.PaymentMethod)).Order().ToList(),
            o.Paid >= o.TotalAmount ? "PAID" : o.Paid > 0 ? "PARTIALLY_PAID" : "UNPAID", EnumText.Format(o.Status))).ToList();
        return new PagedResult<CustomerOrderResponse>(items, request.Page, request.PageSize, total);
    }

    public async Task<PagedResult<CustomerPaymentResponse>> PaymentsAsync(Guid id, PaymentListRequest request, CancellationToken token)
    {
        writes.Actor();
        await RequireCustomerAsync(id, token);
        // The route owns the customer and context; client-supplied ids cannot widen the history scope.
        var page = await paymentQueries.ListAsync(request with { PaymentContext = "DEBT_REPAYMENT", FarmerProfileId = id, OrderId = null }, id, token);
        var ids = page.Items.Select(p => p.Id).ToList();
        var details = await context.Payments.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.ConfirmedBy }).ToDictionaryAsync(p => p.Id, token);
        var allocations = await (from a in context.PaymentAllocations.AsNoTracking()
            join e in context.DebtEntries.AsNoTracking() on a.DebtEntryId equals e.Id into entries
            from e in entries.DefaultIfEmpty()
            where ids.Contains(a.PaymentId) && a.AllocationType == PaymentAllocationType.Debt
            select new { a.PaymentId, a.Id, a.DebtEntryId, EntryNumber = e == null ? null : e.EntryNumber,
                a.AllocatedAmount, a.PrepaymentConsumedAmount, a.Status, a.AllocatedAt }).ToListAsync(token);
        return new PagedResult<CustomerPaymentResponse>(page.Items.Select(p => new CustomerPaymentResponse(p, details[p.Id].ConfirmedBy,
            allocations.Where(a => a.PaymentId == p.Id).OrderBy(a => a.AllocatedAt).ThenBy(a => a.Id)
                .Select(a => new PaymentAllocationResponse(a.Id, "DEBT", null, null, a.DebtEntryId, a.EntryNumber,
                    a.AllocatedAmount, a.PrepaymentConsumedAmount, EnumText.Format(a.Status), a.AllocatedAt)).ToList())).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<IReadOnlyList<AddressResponse>> AddressesAsync(Guid id, CancellationToken token)
    {
        writes.Actor();
        var userId = await context.FarmerProfiles.AsNoTracking()
            .Where(f => f.Id == id && f.User.Role.Code == RoleCode.Farmer)
            .Select(f => (Guid?)f.UserId).FirstOrDefaultAsync(token)
            ?? throw new NotFoundException("Customer", id);
        return await addresses.ListAsync(userId, token);
    }

    private async Task<IQueryable<Row>> RowsAsync(Guid storeId, CancellationToken token)
    {
        var defaultGroup = await context.CustomerGroups.AsNoTracking().Where(g => g.StoreId == storeId && g.IsActive && g.IsDefault)
            .Select(g => (Guid?)g.Id).FirstOrDefaultAsync(token);
        return context.FarmerProfiles.AsNoTracking().Where(f => f.User.Role.Code == RoleCode.Farmer)
            .Select(f => new Row
            {
                Id = f.Id, UserId = f.UserId, FullName = f.User.FullName, Phone = f.User.PhoneNumber, Email = f.User.Email,
                Status = f.User.Status, Notes = f.Notes,
                GroupId = context.CustomerGroupAssignments.Where(a => a.FarmerProfileId == f.Id && a.EffectiveTo == null && a.CustomerGroup.StoreId == storeId)
                    .Select(a => (Guid?)a.CustomerGroupId).FirstOrDefault() ?? defaultGroup,
                Orders = context.Orders.LongCount(o => o.StoreId == storeId && o.FarmerProfileId == f.Id && o.Status != OrderStatus.Cancelled),
                Purchases = context.Orders.Where(o => o.StoreId == storeId && o.FarmerProfileId == f.Id && o.Status == OrderStatus.Completed)
                    .Sum(o => (decimal?)o.TotalAmount) ?? 0m,
                Debt = context.DebtAccounts.Where(a => a.StoreId == storeId && a.FarmerProfileId == f.Id).Select(a => (decimal?)a.CurrentBalance).FirstOrDefault() ?? 0m,
                Limit = context.FarmerCreditProfiles.Where(p => p.StoreId == storeId && p.FarmerProfileId == f.Id).Select(p => (decimal?)p.CreditLimit).FirstOrDefault() ?? 0m,
                CreditEnabled = f.User.Status == UserStatus.Active
                    && context.FarmerCreditProfiles.Any(p => p.StoreId == storeId && p.FarmerProfileId == f.Id && p.Status == FarmerCreditProfileStatus.Active)
                    && context.DebtAccounts.Any(a => a.StoreId == storeId && a.FarmerProfileId == f.Id && a.Status == DebtAccountStatus.Active),
                Reserved = context.CreditReservations.Where(r => r.StoreId == storeId
                    && context.FarmerCreditProfiles.Any(p => p.Id == r.FarmerCreditProfileId && p.StoreId == storeId && p.FarmerProfileId == f.Id)
                    && (r.Status == CreditReservationStatus.Active || r.Status == CreditReservationStatus.PartiallyConsumed))
                    .Sum(r => (decimal?)(r.AmountReserved - r.AmountConsumed - r.AmountReleased)) ?? 0m,
                Term = context.FarmerCreditProfiles.Where(p => p.StoreId == storeId && p.FarmerProfileId == f.Id)
                    .Select(p => (int?)p.CreditTier!.DefaultPaymentTermDays).FirstOrDefault(),
                CreatedAt = f.CreatedAt, UpdatedAt = f.UpdatedAt > f.User.UpdatedAt ? f.UpdatedAt : f.User.UpdatedAt
            });
    }

    private Task<Dictionary<Guid, CustomerReference>> GroupReferencesAsync(IEnumerable<Row> rows, Guid storeId, CancellationToken token)
    {
        var ids = rows.Where(r => r.GroupId != null).Select(r => r.GroupId!.Value).Distinct().ToList();
        return context.CustomerGroups.AsNoTracking().Where(g => g.StoreId == storeId && ids.Contains(g.Id))
            .Select(g => new CustomerReference(g.Id, g.Code, g.Name)).ToDictionaryAsync(g => g.Id, token);
    }

    private static CustomerResponse Map(Row r, Dictionary<Guid, CustomerReference> groups) => new(
        r.Id, r.UserId, null, r.FullName, r.Phone, r.Email, "REGISTERED", r.GroupId is { } id ? groups.GetValueOrDefault(id) : null,
        EnumText.Format(r.Status), r.Notes, r.Orders, r.Purchases, r.Debt, r.Limit, r.CreditEnabled,
        r.Reserved, r.Limit - r.Debt - r.Reserved, r.Term, r.CreatedAt, r.UpdatedAt);

    private async Task<CustomerDebtSummaryResponse> SummaryAsync(Guid id, Guid storeId, Row row, CancellationToken token)
    {
        var today = BusinessCalendar.Today(clock.UtcNow);
        var accounts = context.DebtAccounts.AsNoTracking().Where(a => a.StoreId == storeId && a.FarmerProfileId == id).Select(a => a.Id);
        var overdue = await context.DebtEntries.AsNoTracking()
            .Where(e => accounts.Contains(e.DebtAccountId) && e.DueDate < today && e.OutstandingAmount > 0)
            .SumAsync(e => (decimal?)e.OutstandingAmount, token) ?? 0m;
        var paid = await context.DebtTransactions.AsNoTracking()
            .Where(t => accounts.Contains(t.DebtAccountId)
                && t.TransactionType == DebtTransactionType.Payment && t.Status == DebtTransactionStatus.Posted)
            .SumAsync(t => (decimal?)-t.AmountDelta, token) ?? 0m;
        return new CustomerDebtSummaryResponse(row.Debt, row.Debt, null, overdue, paid, row.Limit, row.Reserved, row.Limit - row.Debt - row.Reserved);
    }

    private async Task ApplyDetailsAsync(FarmerProfile farmer, Guid storeId, CustomerRequest request, CancellationToken token)
    {
        if (request.CustomerGroupId is { } groupId)
        {
            await writes.AssignAsync(farmer.Id, storeId, groupId, "Customer details updated", token);
        }
        if (request.Address is { } a)
        {
            ContactNormalizer.TryNormalizePhone(a.RecipientPhone, out var phone);
            var address = await context.UserAddresses.FirstOrDefaultAsync(x => x.UserId == farmer.UserId && x.IsDefault, token);
            if (address == null)
            {
                address = new UserAddress(farmer.UserId, a.RecipientName.Trim(), phone!, a.AddressLine.Trim(), a.Province.Trim(),
                    AddressType.Home, Texts.Clean(a.Ward), Texts.Clean(a.District));
                address.MarkAsDefault();
                context.UserAddresses.Add(address);
            }
            else
            {
                address.Update(a.RecipientName.Trim(), phone!, a.AddressLine.Trim(), a.Province.Trim(), address.AddressType,
                    Texts.Clean(a.Ward), Texts.Clean(a.District), address.Latitude, address.Longitude);
            }
        }
        await writes.ApplyCreditAsync(farmer.Id, storeId, request, token);
    }

    private static (string? Phone, string? Email) Contact(CustomerRequest request)
    {
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !ContactNormalizer.TryNormalizePhone(request.PhoneNumber, out phone))
        {
            throw new BusinessRuleException("Invalid phone number.");
        }
        return (phone, ContactNormalizer.NormalizeEmail(request.Email));
    }

    private async Task EnsureFreeContactAsync(string? phone, string? email, Guid? except, CancellationToken token)
    {
        if (await context.Users.AsNoTracking().AnyAsync(u => u.Id != except
            && ((phone != null && u.PhoneNumber == phone) || (email != null && u.Email != null && u.Email.ToLower() == email)), token))
        {
            throw new ConflictException("An account with this phone number or email already exists.");
        }
    }

    private async Task RequireCustomerAsync(Guid id, CancellationToken token)
    {
        if (!await context.FarmerProfiles.AsNoTracking().AnyAsync(f => f.Id == id && f.User.Role.Code == RoleCode.Farmer, token))
        {
            throw new NotFoundException("Customer", id);
        }
    }

    private async Task SaveAsync(CancellationToken token)
    {
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateException e) when (databaseErrors.IsUniqueViolation(e))
        {
            throw new ConflictException("Customer contact, group assignment or credit profile already exists.");
        }
    }

    private static object Snapshot(FarmerProfile f) => new { f.User.FullName, f.User.PhoneNumber, f.User.Email, f.Notes };
}
