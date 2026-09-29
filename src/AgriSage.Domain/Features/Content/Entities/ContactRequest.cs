using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Content.Enums;

namespace AgriSage.Domain.Features.Content.Entities;

// Customer support/contact submission; user_id may be null when submitted before login.
// Only the documented invariant is enforced (RESOLVED → resolved_at); no transition graph is defined.
public sealed class ContactRequest : SoftDeletableEntity
{
    private ContactRequest()
    {
    }

    public ContactRequest(
        string requestNumber,
        string contactName,
        string subject,
        string message,
        Guid? userId = null,
        string? contactPhone = null,
        string? contactEmail = null)
    {
        RequestNumber = Guard.NotNullOrWhiteSpace(requestNumber);
        ContactName = Guard.NotNullOrWhiteSpace(contactName);
        Subject = Guard.NotNullOrWhiteSpace(subject);
        Message = Guard.NotNullOrWhiteSpace(message);
        UserId = userId;
        ContactPhone = contactPhone;
        ContactEmail = contactEmail;
        Status = ContactRequestStatus.Open;
    }

    public string RequestNumber { get; private set; } = null!;

    public Guid? UserId { get; private set; }

    public string ContactName { get; private set; } = null!;

    public string? ContactPhone { get; private set; }

    public string? ContactEmail { get; private set; }

    public string Subject { get; private set; } = null!;

    public string Message { get; private set; } = null!;

    public ContactRequestStatus Status { get; private set; }

    public Guid? AssignedTo { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? ResolutionNote { get; private set; }

    public void AssignTo(Guid? assigneeId) => AssignedTo = assigneeId;

    public void Resolve(DateTimeOffset resolvedAt, string? resolutionNote = null)
    {
        Status = ContactRequestStatus.Resolved;
        ResolvedAt = resolvedAt;
        ResolutionNote = resolutionNote;
    }

    // OPEN / IN_PROGRESS / CLOSED; RESOLVED goes through Resolve so resolved_at is always set.
    public void ChangeStatus(ContactRequestStatus status)
    {
        if (status == ContactRequestStatus.Resolved)
        {
            throw new DomainException("Use Resolve to mark a contact request as RESOLVED.");
        }

        Status = status;
    }
}
