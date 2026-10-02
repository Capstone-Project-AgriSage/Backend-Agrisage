using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Common;

// The system operates one active store; its id is never hard-coded.
public static class ActiveStore
{
    public static async Task<Guid> GetIdAsync(IAgriSageDbContext context, CancellationToken cancellationToken)
    {
        var ids = await context.Stores.AsNoTracking()
            .Where(s => s.Status == StoreStatus.Active)
            .Select(s => s.Id).Take(2).ToListAsync(cancellationToken);

        return ids.Count switch
        {
            1 => ids[0],
            0 => throw new BusinessRuleException("No active store is configured."),
            _ => throw new BusinessRuleException("More than one active store exists; the system operates a single store.")
        };
    }
}
