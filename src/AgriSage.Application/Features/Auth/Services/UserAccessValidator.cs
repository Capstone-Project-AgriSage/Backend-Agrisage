using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Auth.Services;

public sealed class UserAccessValidator(IAgriSageDbContext context) : IUserAccessValidator
{
    // Deleted users are hidden by the soft-delete query filter, so they are treated as not active.
    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken);
}
