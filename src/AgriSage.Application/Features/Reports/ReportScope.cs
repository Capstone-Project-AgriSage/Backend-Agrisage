using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

public sealed class ReportScope(IAgriSageDbContext db, ICurrentUserService user, AgriSage.Application.Features.Permissions.IPermissionEvaluator? permissions = null)
{
    // Sale staff need `permission`: REPORTS.READ for every report except today's counter figures (COUNTER_SALES.SELL,
    // decision 2026-11-16).
    public async Task<Guid> GetStoreIdAsync(CancellationToken token, string permission = "REPORTS.READ")
    {
        if (user.UserId is not { } actor) throw new AuthenticationFailedException("Authentication is required.");
        if (user.Role is not ("ADMIN" or "STORE_OWNER" or "SALES_STAFF")) throw new ForbiddenException();
        if (user.Role == "SALES_STAFF" && (permissions is null || !await permissions.HasAsync(permission, token)))
            throw new ForbiddenException();
        var store = await ActiveStore.GetIdAsync(db, token);
        if (user.Role != "ADMIN" && !await db.StoreMembers.AsNoTracking()
            .AnyAsync(m => m.StoreId == store && m.UserId == actor && m.Status == StoreMemberStatus.Active, token))
            throw new ForbiddenException();
        return store;
    }
}
