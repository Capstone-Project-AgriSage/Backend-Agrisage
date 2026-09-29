namespace AgriSage.Application.Common.Interfaces;

// Identity of the authenticated caller, resolved from JWT claims by Infrastructure.
// Roles and special permissions (e.g. can_review_ai) are added with the Auth feature.
public interface ICurrentUserService
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}
