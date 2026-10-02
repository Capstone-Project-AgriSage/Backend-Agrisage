using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Staff.Dtos.Requests;

// Filters: Role (STORE_OWNER / SALES_STAFF / DELIVERY_STAFF), Status (user status: ACTIVE / INACTIVE / SUSPENDED / LOCKED),
// Search (name, phone, email or employee code).
public sealed record StaffListRequest : PaginationRequest
{
    public string? Role { get; init; }

    public string? Status { get; init; }

    public string? Search { get; init; }
}
