using AgriSage.Application.Features.Customers;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence;

// A prerequisite to an EF default-address swap under the Farmer lock and the use-case transaction (same reason as
// CustomerGroupDefaultSwitcher: the partial unique index ux_user_addresses_default cannot defer its check). Only
// pre-clears the old flag; Application still unmarks it through Domain so SaveChanges writes updated_at/version.
public sealed class UserAddressDefaultSwitcher(AgriSageDbContext context) : IUserAddressDefaultSwitcher
{
    public async Task ClearPreviousAsync(Guid userId, Guid addressId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE user_addresses SET is_default = false WHERE user_id = {userId} AND is_default AND id <> {addressId} AND deleted_at IS NULL",
            cancellationToken);
}
