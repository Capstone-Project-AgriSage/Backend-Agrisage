namespace AgriSage.Application.Features.Auth.Dtos.Requests;

// Input of the --create-admin command; read from User Secrets / environment variables, never from appsettings.
public sealed record CreateAdminRequest(string FullName, string Email, string? PhoneNumber, string Password);
