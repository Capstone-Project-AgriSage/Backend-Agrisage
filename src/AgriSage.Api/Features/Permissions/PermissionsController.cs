using AgriSage.Application.Features.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Permissions;

[ApiController, Authorize]
public sealed class PermissionsController(IPermissionEvaluator evaluator, IPermissionConfigurationService configuration) : ControllerBase
{
    [HttpGet("api/me/permissions")]
    public Task<CurrentPermissionsResponse> Current(CancellationToken token) => evaluator.CurrentAsync(token);
    [HttpGet("api/permissions")]
    public Task<IReadOnlyList<PermissionResponse>> Catalog(CancellationToken token) => configuration.CatalogAsync(token);
    [HttpGet("api/roles"), Authorize(Roles = "ADMIN")]
    public Task<IReadOnlyList<RolePermissionsResponse>> Roles(CancellationToken token) => configuration.RolesAsync(token);
    [HttpPut("api/roles/{id:guid}/permissions"), Authorize(Roles = "ADMIN")]
    public Task<RolePermissionsResponse> SetRole(Guid id, SetRolePermissionsRequest request, CancellationToken token) => configuration.SetRoleAsync(id, request, token);
    [HttpGet("api/staff/{userId:guid}/permissions")]
    public Task<MemberPermissionsResponse> Member(Guid userId, CancellationToken token) => configuration.MemberAsync(userId, token);
    [HttpPut("api/staff/{userId:guid}/permissions")]
    public Task<MemberPermissionsResponse> SetMember(Guid userId, SetMemberPermissionsRequest request, CancellationToken token) => configuration.SetMemberAsync(userId, request, token);
}
