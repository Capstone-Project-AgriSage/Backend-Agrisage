namespace AgriSage.Application.Features.Auth.Dtos.Requests;

public sealed record RefreshRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Identifier);
// Exactly one proof: Token (the long token an email carries) or Identifier + Code (the phone and the 6-digit code an
// SMS carries).
public sealed record ResetPasswordRequest(string? Token, string NewPassword, string? Identifier = null, string? Code = null);
public sealed record ConfirmEmailRequest(string Token);
public sealed record ConfirmPhoneRequest(string Code);
