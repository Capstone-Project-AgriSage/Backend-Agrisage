using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Customers;

// FLOW_2 §3.1. Every write locks the Farmer's profile row, so two requests of the same Farmer never race on the
// default flag or the address limit. Someone else's address id is "not found".
public sealed class MyProfileService(IAgriSageDbContext context, ICurrentUserService currentUser,
    IDatabaseErrorClassifier databaseErrors, IRowLockService locks, IUserAddressDefaultSwitcher defaults,
    CustomerAddresses addresses) : IMyProfileService
{
    public const int MaxActiveAddresses = 10;

    public async Task<FarmerProfileResponse> GetProfileAsync(CancellationToken cancellationToken)
    {
        var farmer = await FarmerAsync(tracked: false, cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        // Current assignment, or the default group (decision B-D2).
        var group = await context.CustomerGroupAssignments.AsNoTracking()
                .Where(a => a.FarmerProfileId == farmer.Id && a.EffectiveTo == null && a.CustomerGroup.StoreId == storeId)
                .Select(a => new CustomerReference(a.CustomerGroupId, a.CustomerGroup.Code, a.CustomerGroup.Name))
                .FirstOrDefaultAsync(cancellationToken)
            ?? await context.CustomerGroups.AsNoTracking()
                .Where(g => g.StoreId == storeId && g.IsActive && g.IsDefault)
                .Select(g => new CustomerReference(g.Id, g.Code, g.Name))
                .FirstOrDefaultAsync(cancellationToken);

        return new FarmerProfileResponse(farmer.Id, farmer.UserId, farmer.User.FullName, farmer.User.PhoneNumber,
            farmer.User.Email, farmer.DateOfBirth, farmer.Gender, group, farmer.CreatedAt);
    }

    public async Task<FarmerProfileResponse> UpdateProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        await using (var transaction = await context.BeginTransactionAsync(cancellationToken))
        {
            var farmer = await LockedFarmerAsync(cancellationToken);
            farmer.User.UpdateProfile(request.FullName.Trim(), farmer.User.AvatarUrl);
            farmer.UpdateDetails(request.DateOfBirth, Texts.Clean(request.Gender)?.ToUpperInvariant(), farmer.Notes);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetProfileAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AddressResponse>> ListAddressesAsync(CancellationToken cancellationToken)
    {
        var farmer = await FarmerAsync(tracked: false, cancellationToken);

        return await addresses.ListAsync(farmer.UserId, cancellationToken);
    }

    public async Task<AddressResponse> CreateAddressAsync(AddressRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var farmer = await LockedFarmerAsync(cancellationToken);
        var active = await context.UserAddresses.CountAsync(a => a.UserId == farmer.UserId, cancellationToken);
        if (active >= MaxActiveAddresses)
        {
            throw new BusinessRuleException($"A customer can keep at most {MaxActiveAddresses} addresses.");
        }

        var (name, phone, line, province, type, ward, district) = Values(request);
        var address = new UserAddress(farmer.UserId, name, phone, line, province, type, ward, district,
            request.Latitude, request.Longitude);
        context.UserAddresses.Add(address);

        // The first address becomes the default.
        if (request.IsDefault || active == 0)
        {
            await MoveDefaultAsync(farmer.UserId, address, cancellationToken);
        }

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CustomerAddresses.Map(address);
    }

    // isDefault: true moves the flag here; false never removes it (deleting the default is the only way to have none).
    public async Task<AddressResponse> UpdateAddressAsync(Guid id, AddressRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var farmer = await LockedFarmerAsync(cancellationToken);
        var address = await OwnAddressAsync(farmer.UserId, id, cancellationToken);

        var (name, phone, line, province, type, ward, district) = Values(request);
        address.Update(name, phone, line, province, type, ward, district, request.Latitude, request.Longitude);
        if (request.IsDefault && !address.IsDefault)
        {
            await MoveDefaultAsync(farmer.UserId, address, cancellationToken);
        }

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CustomerAddresses.Map(address);
    }

    // Soft delete. Deleting the default leaves no default (FLOW_2 §3.1).
    public async Task DeleteAddressAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var farmer = await LockedFarmerAsync(cancellationToken);
        var address = await OwnAddressAsync(farmer.UserId, id, cancellationToken);

        address.UnmarkDefault();
        context.UserAddresses.Remove(address);

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AddressResponse> SetDefaultAddressAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var farmer = await LockedFarmerAsync(cancellationToken);
        var address = await OwnAddressAsync(farmer.UserId, id, cancellationToken);

        if (!address.IsDefault)
        {
            await MoveDefaultAsync(farmer.UserId, address, cancellationToken);
            await SaveAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return CustomerAddresses.Map(address);
    }

    // Unmark the previous default through Domain (so updated_at/version are written), pre-clear it in SQL because the
    // partial unique index is checked per statement, then mark the new one.
    private async Task MoveDefaultAsync(Guid userId, UserAddress target, CancellationToken cancellationToken)
    {
        var previous = await context.UserAddresses
            .Where(a => a.UserId == userId && a.IsDefault && a.Id != target.Id)
            .ToListAsync(cancellationToken);
        foreach (var old in previous)
        {
            old.UnmarkDefault();
        }

        await defaults.ClearPreviousAsync(userId, target.Id, cancellationToken);
        target.MarkAsDefault();
    }

    private async Task<FarmerProfile> LockedFarmerAsync(CancellationToken cancellationToken)
    {
        var id = (await FarmerAsync(tracked: false, cancellationToken)).Id;
        await locks.LockFarmerProfileAsync(id, cancellationToken);

        return await FarmerAsync(tracked: true, cancellationToken);
    }

    private async Task<FarmerProfile> FarmerAsync(bool tracked, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var query = context.FarmerProfiles.Include(f => f.User)
            .Where(f => f.UserId == userId && f.User.Role.Code == RoleCode.Farmer);

        return await (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException("This account has no customer profile.");
    }

    private async Task<UserAddress> OwnAddressAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await context.UserAddresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Address", id);

    // The validator has already checked every value; this only normalizes them.
    private static (string Name, string Phone, string Line, string Province, AddressType Type, string? Ward, string? District)
        Values(AddressRequest request)
    {
        ContactNormalizer.TryNormalizePhone(request.RecipientPhone, out var phone);
        EnumText.TryParse<AddressType>(request.AddressType, out var type);

        return (request.RecipientName.Trim(), phone!, request.AddressLine.Trim(), request.Province.Trim(), type,
            Texts.Clean(request.Ward), Texts.Clean(request.District));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("The default address changed at the same time; try again.");
        }
    }
}
