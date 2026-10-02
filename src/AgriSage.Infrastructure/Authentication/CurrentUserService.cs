using AgriSage.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace AgriSage.Infrastructure.Authentication;

// Current caller from the validated JWT. The user id is the `sub` claim (= users.id) and the role is the `role`
// claim; the Api keeps short claim names (MapInboundClaims = false). No request or anonymous caller
// (background/system work) → null; an authenticated caller without a valid `sub` is an error rather than a
// silent null actor.
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            var subject = httpContextAccessor.HttpContext!.User.FindFirst(AgriSageClaimTypes.Subject)?.Value;

            return Guid.TryParse(subject, out var userId)
                ? userId
                : throw new InvalidOperationException("The authenticated user has no valid 'sub' (user id) claim.");
        }
    }

    public string? Role => IsAuthenticated
        ? httpContextAccessor.HttpContext!.User.FindFirst(AgriSageClaimTypes.Role)?.Value
        : null;
}
