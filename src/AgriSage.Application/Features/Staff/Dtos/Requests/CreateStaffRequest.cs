namespace AgriSage.Application.Features.Staff.Dtos.Requests;

// Role is STORE_OWNER, SALES_STAFF or DELIVERY_STAFF. The initial password is chosen by the creator and handed
// to the employee (there is no forced password change yet).
public sealed record CreateStaffRequest(
    string FullName,
    string Role,
    string? PhoneNumber,
    string? Email,
    string Password,
    string? EmployeeCode = null,
    DateOnly? JoinedAt = null);
