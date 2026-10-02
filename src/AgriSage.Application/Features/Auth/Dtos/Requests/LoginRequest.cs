namespace AgriSage.Application.Features.Auth.Dtos.Requests;

// Identifier is an email (contains an at sign) or a Vietnamese mobile number.
public sealed record LoginRequest(string Identifier, string Password);
