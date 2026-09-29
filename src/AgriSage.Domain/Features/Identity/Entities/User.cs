using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Domain.Features.Identity.Entities;

public sealed class User : SoftDeletableEntity
{
    private User()
    {
    }

    public User(
        Guid roleId,
        string fullName,
        string passwordHash,
        string? email,
        string? phoneNumber,
        string? avatarUrl = null,
        UserStatus status = UserStatus.Active)
    {
        RoleId = roleId;
        FullName = Guard.NotNullOrWhiteSpace(fullName);
        PasswordHash = Guard.NotNullOrWhiteSpace(passwordHash);
        SetContact(email, phoneNumber);
        AvatarUrl = avatarUrl;
        Status = status;
    }

    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = null!;

    public string? Email { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string PasswordHash { get; private set; } = null!;

    public string FullName { get; private set; } = null!;

    public string? AvatarUrl { get; private set; }

    public UserStatus Status { get; private set; }

    public bool EmailVerified { get; private set; }

    public bool PhoneVerified { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public void UpdateContact(string? email, string? phoneNumber) => SetContact(email, phoneNumber);

    public void UpdateProfile(string fullName, string? avatarUrl)
    {
        FullName = Guard.NotNullOrWhiteSpace(fullName);
        AvatarUrl = avatarUrl;
    }

    public void ChangePasswordHash(string passwordHash) =>
        PasswordHash = Guard.NotNullOrWhiteSpace(passwordHash);

    public void AssignRole(Guid roleId) => RoleId = roleId;

    public void ChangeStatus(UserStatus status) => Status = status;

    public void MarkEmailVerified()
    {
        if (Email is null)
        {
            throw new DomainException("Cannot verify email: user has no email.");
        }

        EmailVerified = true;
    }

    public void MarkPhoneVerified()
    {
        if (PhoneNumber is null)
        {
            throw new DomainException("Cannot verify phone number: user has no phone number.");
        }

        PhoneVerified = true;
    }

    public void RecordLogin(DateTimeOffset loggedInAt) => LastLoginAt = loggedInAt;

    // At least one of email / phone number must exist.
    private void SetContact(string? email, string? phoneNumber)
    {
        email = string.IsNullOrWhiteSpace(email) ? null : email;
        phoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber;

        if (email is null && phoneNumber is null)
        {
            throw new DomainException("A user must have an email or a phone number.");
        }

        Email = email;
        PhoneNumber = phoneNumber;
    }
}
