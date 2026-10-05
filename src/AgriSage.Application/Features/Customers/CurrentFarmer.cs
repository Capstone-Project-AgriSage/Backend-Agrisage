using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Customers;

// The signed-in Farmer behind /api/me/... (README §1: identity comes only from the JWT, never from the client).
public sealed class CurrentFarmer(IAgriSageDbContext context, ICurrentUserService currentUser)
{
    public sealed record Identity(Guid FarmerProfileId, Guid UserId);

    public async Task<Identity> GetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");

        return await context.FarmerProfiles.AsNoTracking()
                .Where(f => f.UserId == userId && f.User.Role.Code == RoleCode.Farmer)
                .Select(f => new Identity(f.Id, f.UserId))
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException("This account has no customer profile.");
    }
}
