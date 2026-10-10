namespace AgriSage.Application.Features.Auth.Dtos.Responses;

public sealed record CurrentUserResponse(
    Guid Id,
    string FullName,
    string? PhoneNumber,
    string? Email,
    string Role,
    string Status,
    bool PhoneVerified,
    bool EmailVerified,
    // Whether this account may decide AI diagnosis cases (store_members.can_review_ai); web screens use it to show them.
    bool CanReviewAi = false);
