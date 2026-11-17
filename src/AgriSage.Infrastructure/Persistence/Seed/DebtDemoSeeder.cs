using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence.Seed;

// F3.20: a minimal demo data set for the debt-by-season flow, for a LOCAL or test database (the command refuses anything else):
// an owner and a Sale staff member, two seasons (one collecting and past its due date, one open for orders) and four customers —
// a normal debt, an overdue one, a disputed one and one rolled over to the open season. Needs the reference seed (roles, the active
// store). Idempotent: the owner's demo e-mail is the marker. It never starts reminders (T0) and sends nothing.
public sealed class DebtDemoSeeder(AgriSageDbContext context, IPasswordHashService passwords, IDateTimeProvider clock)
{
    public const string OwnerEmail = "demo.owner@agrisage.demo";
    public const string StaffEmail = "demo.staff@agrisage.demo";

    public sealed record Result(bool AlreadySeeded, int Users, int Seasons, int Customers, int Debts);

    public async Task<Result> SeedAsync(string password, CancellationToken token)
    {
        if (await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == OwnerEmail, token))
        {
            return new Result(true, 0, 0, 0, 0);
        }

        var store = await context.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, token)
            ?? throw new InvalidOperationException("No active store: run the reference seed first.");
        var roles = await context.Roles.ToDictionaryAsync(r => r.Code, token);
        foreach (var code in new[] { RoleCode.StoreOwner, RoleCode.SalesStaff, RoleCode.Farmer })
        {
            if (!roles.ContainsKey(code)) throw new InvalidOperationException("Roles are missing: run the reference seed first.");
        }

        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);
        var hash = passwords.Hash(password);
        var owner = new User(roles[RoleCode.StoreOwner].Id, "Demo Owner", hash, OwnerEmail, null);
        var staff = new User(roles[RoleCode.SalesStaff].Id, "Demo Staff", hash, StaffEmail, null);
        context.AddRange(owner, staff, new StoreMember(store.Id, owner.Id), new StoreMember(store.Id, staff.Id));

        // Season A: harvest ended 90 days ago, due date 60 days ago (collecting, overdue). Season B opens on A's harvest start and
        // is open for orders now; its due date is ahead.
        var a = new CropSeason(store.Id, "DEMO-A", "Vụ demo A (đã qua hạn)", "Lúa", today.AddDays(-200), today.AddDays(-120), today.AddDays(-90), 30);
        var b = new CropSeason(store.Id, "DEMO-B", "Vụ demo B (đang nhận đơn)", "Lúa", a.HarvestStart, today.AddDays(30), today.AddDays(60), 30);
        context.AddRange(a, b);

        var tier = new CreditTier(store.Id, "DEMO", "Hạng demo", 20_000_000, 30);
        context.Add(tier);

        var debts = 0;
        DebtEntry Add(string name, string phone, decimal amount, CropSeason season, string number)
        {
            var user = new User(roles[RoleCode.Farmer].Id, name, hash, $"{number.ToLowerInvariant()}@agrisage.demo", phone);
            var farmer = new FarmerProfile(user.Id);
            var account = new DebtAccount(store.Id, farmer.Id);
            var posting = account.CreateManualAdjustmentEntry(number, amount, season.PaymentDueDate, owner.Id, now,
                note: "Dữ liệu demo", originCropSeasonId: season.Id);
            context.AddRange(user, farmer, new FarmerCreditProfile(store.Id, farmer.Id, 20_000_000, owner.Id, now, tier.Id), account,
                posting.Entry, posting.Transaction);
            debts++;
            return posting.Entry;
        }

        Add("Nông dân demo - bình thường", "0900000001", 3_000_000, b, "DEMO-0001");
        Add("Nông dân demo - quá hạn", "0900000002", 5_000_000, a, "DEMO-0002");
        Add("Nông dân demo - khiếu nại", "0900000003", 2_000_000, a, "DEMO-0003").Dispute("Khách cho rằng đã trả một phần", owner.Id);
        Add("Nông dân demo - đã gia hạn", "0900000004", 4_000_000, a, "DEMO-0004")
            .Rollover(b.Id, b.PaymentDueDate, DebtReasonCode.MatMua, "Sâu cuốn lá, mất mùa", owner.Id);

        await context.SaveChangesAsync(token);
        return new Result(false, 2, 2, 4, debts);
    }
}
