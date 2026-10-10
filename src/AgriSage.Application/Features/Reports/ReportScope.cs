using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

public sealed class ReportScope(IAgriSageDbContext db, ICurrentUserService user, AgriSage.Application.Features.Permissions.IPermissionEvaluator? permissions = null)
{
    public async Task<Guid> GetStoreIdAsync(CancellationToken token)
    {
        if (user.UserId is not { } actor) throw new AuthenticationFailedException("Authentication is required.");
        if (user.Role is not ("ADMIN" or "STORE_OWNER" or "SALES_STAFF")) throw new ForbiddenException();
        if (user.Role == "SALES_STAFF" && (permissions is null || !await permissions.HasAsync("REPORTS.READ", token)))
            throw new ForbiddenException();
        var store = await ActiveStore.GetIdAsync(db, token);
        if (user.Role != "ADMIN" && !await db.StoreMembers.AsNoTracking()
            .AnyAsync(m => m.StoreId == store && m.UserId == actor && m.Status == StoreMemberStatus.Active, token))
            throw new ForbiddenException();
        return store;
    }
}
