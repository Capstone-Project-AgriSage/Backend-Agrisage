using AgriSage.Application.Features.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgriSage.Api.Authorization;

public sealed class PermissionFilter(IPermissionEvaluator permissions) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action || PermissionEndpointMap.IsException(action)) return;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) { context.Result = new ChallengeResult(); return; }
        var code = PermissionEndpointMap.CodeFor(action);
        if (code is null || !await permissions.HasAsync(code, context.HttpContext.RequestAborted)) context.Result = new ForbidResult();
    }
}
