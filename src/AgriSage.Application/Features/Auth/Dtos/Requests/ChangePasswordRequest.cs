namespace AgriSage.Application.Features.Auth.Dtos.Requests;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
