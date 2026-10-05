using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Customers;

// FLOW_2 §3.1 against PostgreSQL (every session rolls back). Same collection as CustomersDatabaseTests: both touch
// the store's default customer group.
[Collection(RealDb.WalkInPriceListCollection)]
public class MyProfileDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Tag() => Guid.NewGuid().ToString("N")[..12];

    private static AddressRequest Address(string line = "Ap 3", bool isDefault = false) =>
        new("Nguyen Van A", "+84 901 234 567", line, "Can Tho", "farm", "Tan Phu", "Cai Rang", 10.0452m, 105.7469m, isDefault);

    private sealed class Env(RealDb.Session session, AgriSageDbContext context)
    {
        public AgriSageDbContext Context { get; } = context;
        public RealDb.MutableUser User => session.CurrentUser;
        public Guid StoreId { get; set; }
        public Guid RoleId { get; set; }

        public MyProfileService Me => new(Context, User, new NpgsqlErrorClassifier(), new RowLockService(Context),
            new UserAddressDefaultSwitcher(Context), new CustomerAddresses(Context));

        public CustomerService Staff
        {
            get
            {
                var clock = new DateTimeProvider();
                var audit = new AuditTrail(Context, User, clock);
                return new CustomerService(Context, new PasswordHashService(), clock, new NpgsqlErrorClassifier(),
                    new RowLockService(Context), audit, new CustomerWrites(Context, User, clock, audit),
                    new PaymentQueries(Context), new CustomerAddresses(Context));
            }
        }

        // A new Farmer (user + profile); the session acts as that Farmer.
        public async Task<FarmerProfile> FarmerAsync(string? notes = null)
        {
            var user = new User(RoleId, $"Farmer {Tag()}", "hash", $"{Tag()}@example.test", null);
            var farmer = new FarmerProfile(user.Id, notes: notes);
            Context.Users.Add(user);
            Context.FarmerProfiles.Add(farmer);
            await Context.SaveChangesAsync(Token);
            User.UserId = user.Id;
            User.Role = "FARMER";
            return farmer;
        }
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var e = new Env(session, session.NewContext());
        var role = await e.Context.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.Farmer, Token);
        if (role == null) { role = new Role(RoleCode.Farmer, "Farmer"); e.Context.Roles.Add(role); }
        e.RoleId = role.Id;
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
    public async Task The_first_address_is_default_and_the_flag_moves_in_one_transaction()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        var farmer = await e.FarmerAsync();

        var first = await e.Me.CreateAddressAsync(Address("First"), Token);
        Assert.True(first.IsDefault);
        Assert.Equal("0901234567", first.RecipientPhone);
        Assert.Equal("FARM", first.AddressType);

        var second = await e.Me.CreateAddressAsync(Address("Second"), Token);
        Assert.False(second.IsDefault);

        var third = await e.Me.CreateAddressAsync(Address("Third", isDefault: true), Token);
        Assert.True(third.IsDefault);
        Assert.Equal(third.Id, Assert.Single(await e.Context.UserAddresses.AsNoTracking()
            .Where(a => a.UserId == farmer.UserId && a.IsDefault).ToListAsync(Token)).Id);

        Assert.True((await e.Me.SetDefaultAddressAsync(second.Id, Token)).IsDefault);
        Assert.True((await e.Me.UpdateAddressAsync(first.Id, Address("First renamed", isDefault: true), Token)).IsDefault);

        var list = await e.Me.ListAddressesAsync(Token);
        Assert.Equal(3, list.Count);
        Assert.Equal(first.Id, list[0].Id); // default first
        Assert.Equal("First renamed", list[0].AddressLine);
        Assert.Single(list, a => a.IsDefault);

        // isDefault: false never removes the flag.
        Assert.True((await e.Me.UpdateAddressAsync(first.Id, Address("Still default"), Token)).IsDefault);
    }

    [RealDbFact]
    public async Task Deleting_the_default_leaves_none_and_soft_deletes_the_row()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        var farmer = await e.FarmerAsync();
        var first = await e.Me.CreateAddressAsync(Address("First"), Token);
        await e.Me.CreateAddressAsync(Address("Second"), Token);

        await e.Me.DeleteAddressAsync(first.Id, Token);

        var list = await e.Me.ListAddressesAsync(Token);
        Assert.DoesNotContain(list, a => a.Id == first.Id);
        Assert.DoesNotContain(list, a => a.IsDefault);
        var deleted = await e.Context.UserAddresses.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == first.Id, Token);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(farmer.UserId, deleted.DeletedBy);
        Assert.False(deleted.IsDefault);
        await Assert.ThrowsAsync<NotFoundException>(() => e.Me.SetDefaultAddressAsync(first.Id, Token));
    }

    [RealDbFact]
    public async Task Another_farmers_address_is_not_found()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        await e.FarmerAsync();
        var theirs = await e.Me.CreateAddressAsync(Address(), Token);

        await e.FarmerAsync(); // now acting as a second Farmer
        await Assert.ThrowsAsync<NotFoundException>(() => e.Me.UpdateAddressAsync(theirs.Id, Address(), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => e.Me.DeleteAddressAsync(theirs.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => e.Me.SetDefaultAddressAsync(theirs.Id, Token));
        Assert.Empty(await e.Me.ListAddressesAsync(Token));
    }

    [RealDbFact]
    public async Task At_most_ten_active_addresses()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        await e.FarmerAsync();
        var created = new List<AddressResponse>();
        for (var i = 0; i < MyProfileService.MaxActiveAddresses; i++)
            created.Add(await e.Me.CreateAddressAsync(Address($"Line {i}"), Token));

        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Me.CreateAddressAsync(Address("Eleventh"), Token));

        // A deleted address frees its place.
        await e.Me.DeleteAddressAsync(created[^1].Id, Token);
        Assert.False((await e.Me.CreateAddressAsync(Address("Replacement"), Token)).IsDefault);
    }

    [RealDbFact]
    public async Task Profile_update_changes_name_birth_date_and_gender_but_never_the_staff_note()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        var farmer = await e.FarmerAsync(notes: "Staff only");

        var updated = await e.Me.UpdateProfileAsync(new("  Tran Thi B ", new DateOnly(1980, 5, 1), "female"), Token);

        Assert.Equal(farmer.Id, updated.FarmerProfileId);
        Assert.Equal("Tran Thi B", updated.FullName);
        Assert.Equal(new DateOnly(1980, 5, 1), updated.DateOfBirth);
        Assert.Equal("FEMALE", updated.Gender);
        Assert.Equal("Staff only", (await e.Context.FarmerProfiles.AsNoTracking().SingleAsync(f => f.Id == farmer.Id, Token)).Notes);
    }

    [RealDbFact]
    public async Task Profile_group_is_the_current_assignment_or_the_default_group()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        var farmer = await e.FarmerAsync();
        var defaultGroup = await e.Context.CustomerGroups.FirstOrDefaultAsync(g => g.StoreId == e.StoreId && g.IsActive && g.IsDefault, Token);
        if (defaultGroup == null)
        {
            defaultGroup = new CustomerGroup(e.StoreId, $"D{Tag()}", "Default");
            defaultGroup.SetAsDefault();
            e.Context.CustomerGroups.Add(defaultGroup);
        }
        var regular = new CustomerGroup(e.StoreId, $"R{Tag()}", "Regular");
        e.Context.CustomerGroups.Add(regular);
        await e.Context.SaveChangesAsync(Token);

        Assert.Equal(defaultGroup.Id, (await e.Me.GetProfileAsync(Token)).CustomerGroup?.Id);

        e.Context.CustomerGroupAssignments.Add(new CustomerGroupAssignment(farmer.Id, regular.Id, DateTimeOffset.UtcNow, farmer.UserId));
        await e.Context.SaveChangesAsync(Token);
        Assert.Equal(regular.Id, (await e.Me.GetProfileAsync(Token)).CustomerGroup?.Id);
    }

    [RealDbFact]
    public async Task A_user_without_a_farmer_profile_is_forbidden()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        e.User.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Me.GetProfileAsync(Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Me.CreateAddressAsync(Address(), Token));
    }

    [RealDbFact]
    public async Task Staff_see_a_customers_addresses()
    {
        await using var session = await RealDb.Session.StartAsync();
        var e = await PrepareAsync(session);
        var farmer = await e.FarmerAsync();
        var address = await e.Me.CreateAddressAsync(Address(), Token);

        e.User.Role = "SALES_STAFF";
        Assert.Equal(address.Id, Assert.Single(await e.Staff.AddressesAsync(farmer.Id, Token)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => e.Staff.AddressesAsync(Guid.NewGuid(), Token));
    }
}
