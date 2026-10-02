using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Application.Features.Staff.Dtos.Responses;
using AgriSage.Application.Features.Staff.Interfaces;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Staff.Services;

// Staff = users with a staff role who are members of the single active store. A create writes the User and the
// StoreMember in one SaveChanges (one transaction). Rules: StaffPolicy; the caller's role comes from the JWT.
public sealed class StaffService(
    IAgriSageDbContext context,
    IPasswordHashService passwordHasher,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors) : IStaffService
{
    private sealed record Actor(Guid Id, RoleCode Role);

    private sealed record StaffRow(
        Guid UserId,
        string FullName,
        string? PhoneNumber,
        string? Email,
        RoleCode Role,
        UserStatus Status,
        StoreMemberStatus MemberStatus,
        string? EmployeeCode,
        DateOnly? JoinedAt,
        DateOnly? LeftAt,
        bool CanReviewAi,
        DateTimeOffset CreatedAt);

    public async Task<StaffResponse> CreateAsync(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var actor = GetActor();
        RoleCodeFormat.TryParse(request.Role, out var roleCode);
        if (!StaffPolicy.CanManage(actor.Role, roleCode))
        {
            throw new ForbiddenException("You are not allowed to create an account with this role.");
        }

        var storeId = await GetActiveStoreIdAsync(cancellationToken);
        var (phone, email) = Contact(request.PhoneNumber, request.Email);
        await EnsureContactIsFreeAsync(phone, email, null, cancellationToken);

        var role = await context.Roles.FirstOrDefaultAsync(r => r.Code == roleCode && r.IsActive, cancellationToken)
            ?? throw new BusinessRuleException($"The {RoleCodeFormat.ToText(roleCode)} role is not configured.");

        var user = new User(role.Id, request.FullName.Trim(), passwordHasher.Hash(request.Password), email, phone);
        var member = new StoreMember(storeId, user.Id, Clean(request.EmployeeCode), request.JoinedAt);
        context.Users.Add(user);
        context.StoreMembers.Add(member);
        await SaveAsync(cancellationToken);

        return ToResponse(user, role.Code, member);
    }

    public async Task<PagedResult<StaffResponse>> ListAsync(StaffListRequest request, CancellationToken cancellationToken)
    {
        GetActor();
        var storeId = await GetActiveStoreIdAsync(cancellationToken);

        var staffRoles = StaffPolicy.StaffRoles.ToArray();
        var query = context.StoreMembers.AsNoTracking()
            .Where(m => m.StoreId == storeId && staffRoles.Contains(m.User.Role.Code));

        if (RoleCodeFormat.TryParse(request.Role, out var roleFilter))
        {
            query = query.Where(m => m.User.Role.Code == roleFilter);
        }

        if (Enum.TryParse<UserStatus>(request.Status, ignoreCase: true, out var statusFilter))
        {
            query = query.Where(m => m.User.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(m =>
                m.User.FullName.ToLower().Contains(term)
                || (m.User.PhoneNumber != null && m.User.PhoneNumber.Contains(term))
                || (m.User.Email != null && m.User.Email.ToLower().Contains(term))
                || (m.EmployeeCode != null && m.EmployeeCode.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderBy(m => m.User.FullName).ThenBy(m => m.UserId)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(m => new StaffRow(
                m.UserId, m.User.FullName, m.User.PhoneNumber, m.User.Email, m.User.Role.Code, m.User.Status,
                m.Status, m.EmployeeCode, m.JoinedAt, m.LeftAt, m.CanReviewAi, m.User.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<StaffResponse>(rows.Select(ToResponse).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<StaffResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        GetActor();
        var member = await LoadStaffAsync(userId, cancellationToken);

        return ToResponse(member.User, member.User.Role.Code, member);
    }

    public async Task<StaffResponse> UpdateAsync(Guid userId, UpdateStaffRequest request, CancellationToken cancellationToken)
    {
        var member = await LoadManagedAsync(userId, cancellationToken);
        var user = member.User;
        var (phone, email) = Contact(request.PhoneNumber, request.Email);
        await EnsureContactIsFreeAsync(phone, email, user.Id, cancellationToken);

        user.UpdateProfile(request.FullName.Trim(), user.AvatarUrl);
        user.UpdateContact(email, phone);
        member.UpdateEmployment(Clean(request.EmployeeCode), request.JoinedAt);
        await SaveAsync(cancellationToken);

        return ToResponse(user, user.Role.Code, member);
    }

    public async Task LockAsync(Guid userId, CancellationToken cancellationToken)
    {
        var member = await LoadManagedAsync(userId, cancellationToken);
        EnsureNotSelf(member, "lock");

        member.User.ChangeStatus(UserStatus.Locked);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlockAsync(Guid userId, CancellationToken cancellationToken)
    {
        var member = await LoadManagedAsync(userId, cancellationToken);

        member.User.ChangeStatus(UserStatus.Active);
        if (member.Status != StoreMemberStatus.Active)
        {
            member.Activate();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(Guid userId, ResetStaffPasswordRequest request, CancellationToken cancellationToken)
    {
        var member = await LoadManagedAsync(userId, cancellationToken);
        if (member.UserId == currentUser.UserId)
        {
            throw new BusinessRuleException("Use change-password to change your own password.");
        }

        member.User.ChangePasswordHash(passwordHasher.Hash(request.NewPassword));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var member = await LoadManagedAsync(userId, cancellationToken);
        EnsureNotSelf(member, "remove");

        if (member.Status != StoreMemberStatus.Left)
        {
            member.Leave(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
        }

        member.User.ChangeStatus(UserStatus.Locked);
        await context.SaveChangesAsync(cancellationToken);
    }

    private Actor GetActor()
    {
        var id = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        if (!RoleCodeFormat.TryParse(currentUser.Role, out var role) || !StaffPolicy.CanUseStaffApi(role))
        {
            throw new ForbiddenException();
        }

        return new Actor(id, role);
    }

    // Tracked member with its user and role; the target must be staff of the active store.
    private async Task<StoreMember> LoadStaffAsync(Guid userId, CancellationToken cancellationToken)
    {
        var storeId = await GetActiveStoreIdAsync(cancellationToken);
        var member = await context.StoreMembers
            .Include(m => m.User).ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.StoreId == storeId, cancellationToken);

        return member is not null && StaffPolicy.IsStaffRole(member.User.Role.Code)
            ? member
            : throw new NotFoundException("Staff member", userId);
    }

    private async Task<StoreMember> LoadManagedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var actor = GetActor();
        var member = await LoadStaffAsync(userId, cancellationToken);

        return StaffPolicy.CanManage(actor.Role, member.User.Role.Code)
            ? member
            : throw new ForbiddenException("You are not allowed to manage this account.");
    }

    private void EnsureNotSelf(StoreMember member, string action)
    {
        if (member.UserId == currentUser.UserId)
        {
            throw new BusinessRuleException($"You cannot {action} your own account.");
        }
    }

    private Task<Guid> GetActiveStoreIdAsync(CancellationToken cancellationToken) =>
        ActiveStore.GetIdAsync(context, cancellationToken);

    private async Task EnsureContactIsFreeAsync(string? phone, string? email, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var taken = await context.Users.AsNoTracking().AnyAsync(
            u => u.Id != exceptUserId
                && ((phone != null && u.PhoneNumber == phone) || (email != null && u.Email != null && u.Email.ToLower() == email)),
            cancellationToken);

        if (taken)
        {
            throw new ConflictException("An account with this phone number or email already exists.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("An account with this phone number or email already exists.");
        }
    }

    private static (string? Phone, string? Email) Contact(string? phoneNumber, string? email)
    {
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            phone = ContactNormalizer.TryNormalizePhone(phoneNumber, out var normalized)
                ? normalized
                : throw new BusinessRuleException("Phone number must be a valid Vietnamese mobile number.");
        }

        return (phone, ContactNormalizer.NormalizeEmail(email));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StaffResponse ToResponse(User user, RoleCode role, StoreMember member) => new(
        user.Id, user.FullName, user.PhoneNumber, user.Email, RoleCodeFormat.ToText(role),
        user.Status.ToString().ToUpperInvariant(), member.Status.ToString().ToUpperInvariant(),
        member.EmployeeCode, member.JoinedAt, member.LeftAt, member.CanReviewAi, user.CreatedAt);

    private static StaffResponse ToResponse(StaffRow row) => new(
        row.UserId, row.FullName, row.PhoneNumber, row.Email, RoleCodeFormat.ToText(row.Role),
        row.Status.ToString().ToUpperInvariant(), row.MemberStatus.ToString().ToUpperInvariant(),
        row.EmployeeCode, row.JoinedAt, row.LeftAt, row.CanReviewAi, row.CreatedAt);
}
