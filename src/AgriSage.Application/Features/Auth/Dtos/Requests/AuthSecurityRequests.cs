namespace AgriSage.Application.Features.Auth.Dtos.Requests;

public sealed record RefreshRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Identifier);
// Exactly one proof: Token (the long token an email carries) or Identifier + Code (the phone and the 6-digit code an
// SMS carries).
public sealed record ResetPasswordRequest(string? Token, string NewPassword, string? Identifier = null, string? Code = null);
// Checks an SMS code without using it, so a client can confirm the code before it asks for the new password.
public sealed record VerifyResetCodeRequest(string Identifier, string Code);
public sealed record ConfirmEmailRequest(string Token);
public sealed record ConfirmPhoneRequest(string Code);
