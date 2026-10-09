namespace AgriSage.Application.Features.Auth.Dtos.Requests;

public sealed record RefreshRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Identifier);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record ConfirmEmailRequest(string Token);
public sealed record ConfirmPhoneRequest(string Code);
