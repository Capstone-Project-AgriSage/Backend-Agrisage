namespace AgriSage.Application.Common.Interfaces;

// Identity of the authenticated caller, resolved from JWT claims by Infrastructure.
// Special permissions (e.g. can_review_ai) are added with the features that need them.
public interface ICurrentUserService
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    // Role code from the token (FARMER, STORE_OWNER, SALES_STAFF, DELIVERY_STAFF, ADMIN); null when unauthenticated.
    string? Role => null;

    Guid? SessionId => null;
}
