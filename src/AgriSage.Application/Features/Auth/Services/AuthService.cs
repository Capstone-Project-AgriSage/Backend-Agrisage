using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Dtos.Responses;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Auth.Services;

// Register / login use cases. Register creates the User and its FarmerProfile in one SaveChanges (one transaction).
public sealed class AuthService(
    IAgriSageDbContext context,
    IPasswordHashService passwordHasher,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit,
    AuthSessionService sessions,
    IAuthSecurityLock locks) : IAuthService
{
    public async Task<AuthResponse> RegisterFarmerAsync(RegisterFarmerRequest request, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.PhoneNumber);
        var email = ContactNormalizer.NormalizeEmail(request.Email);

        await EnsureContactIsFreeAsync(phone, email, cancellationToken);

        var role = await context.Roles
            .FirstOrDefaultAsync(r => r.Code == RoleCode.Farmer && r.IsActive, cancellationToken)
            ?? throw new BusinessRuleException("The Farmer role is not configured.");

        var user = new User(role.Id, request.FullName.Trim(), passwordHasher.Hash(request.Password), email, phone);
        user.RecordLogin(clock.UtcNow);

        context.Users.Add(user);
        context.FarmerProfiles.Add(new FarmerProfile(user.Id));
        var response = sessions.Start(user, RoleCodeFormat.ToText(role.Code));
        audit.Record("AUTH_REGISTERED", "USER", user.Id, null, newValues: new { response.SessionId });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two registrations raced past the check above; the unique index decided.
            throw new ConflictException("An account with this phone number or email already exists.");
        }

        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await FindByIdentifierAsync(request.Identifier.Trim(), cancellationToken);

        await using var tx = await context.BeginTransactionAsync(cancellationToken);
        if (user is not null)
        {
            await locks.LockUserAsync(user.Id, cancellationToken);
            user = await context.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
        }

        // Unknown account and wrong password are indistinguishable (same message, same work).
        var verification = passwordHasher.Verify(user?.PasswordHash, request.Password);
        if (user is null || verification == PasswordVerification.Failed)
        {
            throw new AuthenticationFailedException("Invalid phone number, email or password.");
        }

        if (user.Status != UserStatus.Active || !user.Role.IsActive)
        {
            throw new ForbiddenException("This account is not active.");
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(passwordHasher.Hash(request.Password));
        }

        user.RecordLogin(clock.UtcNow);
        var response = sessions.Start(user, RoleCodeFormat.ToText(user.Role.Code));
        audit.Record("AUTH_LOGIN", "USER", user.Id, null, newValues: new { response.SessionId });
        await context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return response;
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");

        var user = await context.Users.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthenticationFailedException("Authentication is required.");

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException("This account is not active.");
        }

        return new CurrentUserResponse(
            user.Id,
            user.FullName,
            user.PhoneNumber,
            user.Email,
            RoleCodeFormat.ToText(user.Role.Code),
            user.Status.ToString().ToUpperInvariant(),
            user.PhoneVerified,
            user.EmailVerified);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");

        await using var tx = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockUserAsync(userId, cancellationToken);
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthenticationFailedException("Authentication is required.");

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException("This account is not active.");
        }

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerification.Failed)
        {
            throw new AuthenticationFailedException("The current password is incorrect.");
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            throw new BusinessRuleException("The new password must be different from the current password.");
        }

        user.ChangePasswordHash(passwordHasher.Hash(request.NewPassword));
        audit.Record("PASSWORD_CHANGED", "USER", user.Id, storeId: null); // never the password or its hash
        await context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private async Task<User?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        if (identifier.Contains('@'))
        {
            var email = ContactNormalizer.NormalizeEmail(identifier);
            return await context.Users.AsNoTracking().Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email, cancellationToken);
        }

        return ContactNormalizer.TryNormalizePhone(identifier, out var phone)
            ? await context.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken)
            : null;
    }

    private async Task EnsureContactIsFreeAsync(string? phone, string? email, CancellationToken cancellationToken)
    {
        var taken = await context.Users.AsNoTracking().AnyAsync(
            u => (phone != null && u.PhoneNumber == phone) || (email != null && u.Email != null && u.Email.ToLower() == email),
            cancellationToken);

        if (taken)
        {
            throw new ConflictException("An account with this phone number or email already exists.");
        }
    }

    private static string? NormalizePhone(string? input) =>
        string.IsNullOrWhiteSpace(input) ? null
        : ContactNormalizer.TryNormalizePhone(input, out var phone) ? phone
        : throw new BusinessRuleException("Phone number must be a valid Vietnamese mobile number.");

}
