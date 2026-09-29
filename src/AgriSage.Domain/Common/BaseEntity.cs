namespace AgriSage.Domain.Common;

public abstract class BaseEntity
{
    protected BaseEntity()
    {
        // Time-ordered UUID v7 keeps primary-key indexes compact; compatible with PostgreSQL uuid.
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }
}
