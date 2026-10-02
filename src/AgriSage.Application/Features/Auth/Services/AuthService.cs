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
    IAccessTokenService accessTokens,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors) : IAuthService
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

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two registrations raced past the check above; the unique index decided.
            throw new ConflictException("An account with this phone number or email already exists.");
        }

        return ToAuthResponse(user, RoleCodeFormat.ToText(role.Code));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await FindByIdentifierAsync(request.Identifier.Trim(), cancellationToken);

        // Unknown account and wrong password are indistinguishable (same message, same work).
        var verification = passwordHasher.Verify(user?.PasswordHash, request.Password);
        if (user is null || verification == PasswordVerification.Failed)
        {
            throw new AuthenticationFailedException("Invalid phone number, email or password.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException("This account is not active.");
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(passwordHasher.Hash(request.Password));
        }

        user.RecordLogin(clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken);

        return ToAuthResponse(user, RoleCodeFormat.ToText(user.Role.Code));
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

    private async Task<User?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        if (identifier.Contains('@'))
        {
            var email = ContactNormalizer.NormalizeEmail(identifier);
            return await context.Users.Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email, cancellationToken);
        }

        return ContactNormalizer.TryNormalizePhone(identifier, out var phone)
            ? await context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken)
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

    private AuthResponse ToAuthResponse(User user, string roleCode)
    {
        var token = accessTokens.Issue(user.Id, roleCode);

        return new AuthResponse(
            token.Value,
            token.ExpiresAt,
            new AuthUserResponse(user.Id, user.FullName, user.PhoneNumber, user.Email, roleCode));
    }
}
