using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Auth.Services;

public sealed class AdminBootstrapService(
    IAgriSageDbContext context,
    IPasswordHashService passwordHasher,
    IDatabaseErrorClassifier databaseErrors) : IAdminBootstrapService
{
    public async Task<AdminBootstrapResult> CreateFirstAdminAsync(
        CreateAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (await context.Users.AsNoTracking().AnyAsync(u => u.Role.Code == RoleCode.Admin, cancellationToken))
        {
            return AdminBootstrapResult.AlreadyExists;
        }

        var email = ContactNormalizer.NormalizeEmail(request.Email);
        ContactNormalizer.TryNormalizePhone(request.PhoneNumber, out var phone);

        var taken = await context.Users.AsNoTracking().AnyAsync(
            u => (u.Email != null && u.Email.ToLower() == email) || (phone != null && u.PhoneNumber == phone),
            cancellationToken);
        if (taken)
        {
            throw new ConflictException("An account with this email or phone number already exists.");
        }

        var role = await context.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.Admin && r.IsActive, cancellationToken)
            ?? throw new BusinessRuleException("The Admin role is not configured; run the reference seed first.");

        context.Users.Add(new User(role.Id, request.FullName.Trim(), passwordHasher.Hash(request.Password), email, phone));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("An account with this email or phone number already exists.");
        }

        return AdminBootstrapResult.Created;
    }
}
