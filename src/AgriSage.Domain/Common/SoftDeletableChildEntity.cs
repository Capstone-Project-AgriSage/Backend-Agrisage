using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Common;

// Base for entities that exist only inside another entity's aggregate (coding rule #62).
// Public soft delete always fails; only the aggregate root can remove the child.
public abstract class SoftDeletableChildEntity : SoftDeletableEntity
{
    protected sealed override void EnsureCanBeDeleted() =>
        throw new DomainException($"{GetType().Name} can only be removed through its aggregate root.");

    internal void RemoveFromAggregate(Guid? deletedBy, DateTimeOffset deletedAt) => SoftDelete(deletedBy, deletedAt);
}
