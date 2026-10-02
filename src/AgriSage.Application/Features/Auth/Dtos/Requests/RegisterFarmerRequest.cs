namespace AgriSage.Application.Features.Auth.Dtos.Requests;

public sealed record RegisterFarmerRequest(string FullName, string? PhoneNumber, string? Email, string Password);
