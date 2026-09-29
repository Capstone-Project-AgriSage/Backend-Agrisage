using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Common;

// Shared mapping of the columns every table has (database design §0.1, §0.6, §0.7):
// id uuid DEFAULT gen_random_uuid(); created_at/updated_at; deleted_at/deleted_by (FK users).
// Timestamps and soft-delete values are filled by AGRI-13 interceptors, not here.
internal abstract class EntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : BaseEntity
{
    protected abstract string TableName { get; }

    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id)
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedNever();

        if (typeof(TEntity).IsAssignableTo(typeof(AuditableEntity)))
        {
            builder.Property<DateTimeOffset>(nameof(AuditableEntity.CreatedAt)).IsRequired();
            builder.Property<DateTimeOffset>(nameof(AuditableEntity.UpdatedAt)).IsRequired();
        }

        if (typeof(TEntity).IsAssignableTo(typeof(SoftDeletableEntity)))
        {
            builder.Ignore(nameof(SoftDeletableEntity.IsDeleted));
            builder.HasOne<User>().WithMany().HasForeignKey(nameof(SoftDeletableEntity.DeletedBy));
        }

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);
}
