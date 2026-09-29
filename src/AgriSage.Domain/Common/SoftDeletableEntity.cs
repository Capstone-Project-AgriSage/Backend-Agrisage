using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Common;

public abstract class SoftDeletableEntity : AuditableEntity
{
    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public void MarkDeleted(Guid? deletedBy, DateTimeOffset deletedAt)
    {
        EnsureNotDeleted();
        EnsureCanBeDeleted();
        SoftDelete(deletedBy, deletedAt);
    }

    // Override to block soft delete in states where the record must be kept, e.g. confirmed transactions.
    protected virtual void EnsureCanBeDeleted()
    {
    }

    // Soft delete without the EnsureCanBeDeleted hook; used by aggregate roots removing their own children.
    protected void SoftDelete(Guid? deletedBy, DateTimeOffset deletedAt)
    {
        EnsureNotDeleted();

        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new DomainException($"{GetType().Name} '{Id}' is already deleted.");
        }
    }
}
