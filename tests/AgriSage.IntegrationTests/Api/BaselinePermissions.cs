using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Permissions;

namespace AgriSage.IntegrationTests.Api;

// Offline HTTP fixtures preserve the existing role baseline; live persistence has separate tests.
internal sealed class BaselinePermissions(ICurrentUserService user) : IPermissionEvaluator
{
    public Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token) => Task.FromResult(
        new CurrentPermissionsResponse(user.Role ?? "", null, 0, null,
            PermissionCatalog.Entries.Where(p => user.Role == "ADMIN" || p.DefaultRoles.Contains(user.Role)).Select(p => p.Code).ToList()));
    public async Task<bool> HasAsync(string code, CancellationToken token) => (await CurrentAsync(token)).Permissions.Contains(code);
}
