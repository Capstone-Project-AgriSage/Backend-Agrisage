using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Dtos.Responses;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriSage.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthSecurityController(IAuthSessionService sessions, IAuthChallengeService challenges) : ControllerBase
{
    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public Task<AuthResponse> Refresh(RefreshRequest request, CancellationToken token) => sessions.RefreshAsync(request.RefreshToken, token);

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken token) { await sessions.LogoutAsync(token); return NoContent(); }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken token) { await sessions.LogoutAllAsync(token); return NoContent(); }

    [HttpGet("sessions")]
    public Task<IReadOnlyList<AuthSessionResponse>> Sessions(CancellationToken token) => sessions.ListAsync(token);

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken token) { await sessions.RevokeAsync(id, token); return NoContent(); }

    [HttpPost("forgot-password"), AllowAnonymous, EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest request, CancellationToken token)
    { await challenges.RequestResetAsync(request.Identifier, token); return Accepted(); }

    [HttpPost("reset-password"), AllowAnonymous, EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> Reset(ResetPasswordRequest request, CancellationToken token)
    { await challenges.ResetPasswordAsync(request.Token, request.NewPassword, token); return NoContent(); }

    [HttpPost("email-verification/request"), EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> RequestEmail(CancellationToken token)
    { await challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, token); return Accepted(); }

    [HttpPost("email-verification/confirm"), EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken token)
    { await challenges.ConfirmEmailAsync(request.Token, token); return NoContent(); }

    [HttpPost("phone-verification/request"), EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> RequestPhone(CancellationToken token)
    { await challenges.RequestVerificationAsync(AuthDeliveryChannel.Sms, token); return Accepted(); }

    [HttpPost("phone-verification/confirm"), EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> ConfirmPhone(ConfirmPhoneRequest request, CancellationToken token)
    { await challenges.ConfirmPhoneAsync(request.Code, token); return NoContent(); }
}
