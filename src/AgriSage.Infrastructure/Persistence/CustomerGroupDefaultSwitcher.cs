using AgriSage.Application.Features.Customers;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence;

// A prerequisite to an EF default-group swap under the store lock and the use-case transaction. PostgreSQL's
// partial unique index cannot defer its check. Only pre-clear the old flag; both groups are still changed through
// Domain and SaveChanges, so normal interceptors supply updated_at and the caller stages their audit logs.
// Failure rolls this preparatory statement back too.
public sealed class CustomerGroupDefaultSwitcher(AgriSageDbContext context) : ICustomerGroupDefaultSwitcher
{
    public async Task ClearPreviousAsync(Guid storeId, Guid groupId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE customer_groups SET is_default = false WHERE store_id = {storeId} AND is_default AND id <> {groupId} AND deleted_at IS NULL",
            cancellationToken);
}
