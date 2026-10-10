using AgriSage.Application.Features.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace AgriSage.Api.Authorization;

// Grants can admit Sale to a delegable business action formerly reserved to managers. The MVC permission filter
// always runs too: the legacy role handler succeeding by itself cannot override a revoked effective permission.
public sealed class DelegatedPermissionHandler(IPermissionEvaluator permissions) : AuthorizationHandler<RolesAuthorizationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RolesAuthorizationRequirement requirement)
    {
        if (context.Resource is not HttpContext http || !context.User.IsInRole("SALES_STAFF")) return;
        var action = http.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action is null || PermissionEndpointMap.IsException(action)) return;
        var code = PermissionEndpointMap.CodeFor(action);
        if (code != null && requirement.AllowedRoles.Contains("STORE_OWNER")
            && PermissionCatalog.ByCode.TryGetValue(code, out var definition) && definition.Delegable
            && await permissions.HasAsync(code, http.RequestAborted)) context.Succeed(requirement);
    }
}
