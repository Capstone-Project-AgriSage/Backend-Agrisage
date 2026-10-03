namespace AgriSage.Domain.Common;

public abstract class BaseEntity
{
    protected BaseEntity()
    {
        // Time-ordered UUID v7 keeps primary-key indexes compact and is increasing in creation order, also for rows
        // saved together (same created_at); compatible with PostgreSQL uuid.
        Id = TimeOrderedId.New();
    }

    public Guid Id { get; private set; }
}
