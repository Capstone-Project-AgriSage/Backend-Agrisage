using System.Linq.Expressions;
using AgriSage.Domain.Features.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Common;

internal static class MappingExtensions
{
    // numeric(18,2): money (database design §0.3).
    public static PropertyBuilder<TProperty> IsMoney<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasPrecision(18, 2);

    // numeric(20,6): internal base-unit cost values.
    public static PropertyBuilder<TProperty> IsUnitCost<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasPrecision(20, 6);

    // numeric(10,7): latitude / longitude.
    public static PropertyBuilder<TProperty> IsCoordinate<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasPrecision(10, 7);

    public static PropertyBuilder<TProperty> IsText<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasColumnType("text");

    public static PropertyBuilder<TProperty> IsJson<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasColumnType("jsonb");

    public static PropertyBuilder<TProperty> IsCurrencyCode<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasColumnType("char(3)").HasMaxLength(3).IsFixedLength();

    // Documented DB DEFAULT kept in the schema, while EF always writes the Domain's value
    // (avoids the CLR-default sentinel problem, e.g. is_active DEFAULT true but Domain sets false).
    public static PropertyBuilder<TProperty> HasDbDefault<TProperty>(this PropertyBuilder<TProperty> property, object value) =>
        property.HasDefaultValue(value).ValueGeneratedNever();

    // FK to users that records an actor (created_by, confirmed_by, ...). Actor FKs are not indexed.
    public static void HasUserReference<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> foreignKey)
        where TEntity : class =>
        builder.HasOne<User>().WithMany().HasForeignKey(foreignKey);

    // FK without a Domain navigation.
    public static void HasReference<TEntity, TPrincipal>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> foreignKey)
        where TEntity : class
        where TPrincipal : class =>
        builder.HasOne<TPrincipal>().WithMany().HasForeignKey(foreignKey);

    public static void HasCheck<TEntity>(this EntityTypeBuilder<TEntity> builder, string name, string sql)
        where TEntity : class =>
        builder.ToTable(table => table.HasCheckConstraint($"ck_{builder.Metadata.GetTableName()}_{name}", sql));
}
