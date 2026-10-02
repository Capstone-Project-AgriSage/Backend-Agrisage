namespace AgriSage.Application.Features.Staff.Dtos.Responses;

// Status is the account status (ACTIVE / INACTIVE / SUSPENDED / LOCKED); MemberStatus is the store membership
// (ACTIVE / INACTIVE / LEFT). The password hash is never returned.
public sealed record StaffResponse(
    Guid Id,
    string FullName,
    string? PhoneNumber,
    string? Email,
    string Role,
    string Status,
    string MemberStatus,
    string? EmployeeCode,
    DateOnly? JoinedAt,
    DateOnly? LeftAt,
    bool CanReviewAi,
    DateTimeOffset CreatedAt);
