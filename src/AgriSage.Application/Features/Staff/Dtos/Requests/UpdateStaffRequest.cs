namespace AgriSage.Application.Features.Staff.Dtos.Requests;

public sealed record UpdateStaffRequest(
    string FullName,
    string? PhoneNumber,
    string? Email,
    string? EmployeeCode = null,
    DateOnly? JoinedAt = null);
