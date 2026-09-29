using System.Linq.Expressions;
using System.Text.RegularExpressions;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AgriSage.Infrastructure.Persistence.Conventions;

// Model-wide rules applied after all entity configurations (database design §0, §XXIX, §35.14, §35.15).
internal static partial class PersistenceConventions
{
    private const int MaxIdentifierLength = 63;

    // FKs to users that are ownership/operational (indexed); every other FK to users only records an actor.
    private static readonly HashSet<string> OperationalUserForeignKeys = ["UserId", "ActorUserId"];

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            ApplyEnumConversionsAndChecks(entityType);
            ApplyColumnNames(entityType);
            ApplySoftDeleteQueryFilter(entityType);
            ApplyConcurrencyToken(entityType);
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            ApplyDeleteBehaviorAndForeignKeyIndexes(entityType);
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            ApplyConstraintNames(entityType);
        }
    }

    public static string ToSnakeCase(string name) => WordBoundary().Replace(name, "_$1").ToLowerInvariant();

    private static void ApplyEnumConversionsAndChecks(IMutableEntityType entityType)
    {
        foreach (var property in entityType.GetDeclaredProperties())
        {
            var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

            if (!enumType.IsEnum)
            {
                continue;
            }

            var converter = (ValueConverter)Activator.CreateInstance(
                typeof(UpperSnakeEnumConverter<>).MakeGenericType(enumType))!;
            property.SetValueConverter(converter);

            var column = ToSnakeCase(property.Name);
            var values = string.Join(", ", EnumDbValue.AllValues(enumType).Select(value => $"'{value}'"));
            entityType.AddCheckConstraint(
                Truncate($"ck_{entityType.GetTableName()}_{column}"),
                $"{column} IN ({values})");
        }
    }

    private static void ApplyColumnNames(IMutableEntityType entityType)
    {
        foreach (var property in entityType.GetDeclaredProperties())
        {
            property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    // Every soft-deletable entity, aggregate children included, hides deleted rows (coding rule #25);
    // IgnoreQueryFilters() is the explicit escape hatch. audit_logs has no deleted_at and no filter.
    private static void ApplySoftDeleteQueryFilter(IMutableEntityType entityType)
    {
        if (!entityType.ClrType.IsAssignableTo(typeof(SoftDeletableEntity)))
        {
            return;
        }

        var entity = Expression.Parameter(entityType.ClrType, "entity");
        entityType.SetQueryFilter(Expression.Lambda(
            Expression.Equal(
                Expression.Property(entity, nameof(SoftDeletableEntity.DeletedAt)),
                Expression.Constant(null, typeof(DateTimeOffset?))),
            entity));
    }

    // Only the tables with a `version bigint` column (database design §0.8, §35.15). Selected by interface,
    // not by name: AiModel/AiPolicyConfig.Version are business version strings.
    private static void ApplyConcurrencyToken(IMutableEntityType entityType)
    {
        if (entityType.ClrType.IsAssignableTo(typeof(IHasConcurrencyVersion)))
        {
            entityType.FindProperty(nameof(IHasConcurrencyVersion.Version))!.IsConcurrencyToken = true;
        }
    }

    // Required business FK → RESTRICT; optional historical FK → NO ACTION; never cascade (database design §29.1).
    // EF's automatic FK index convention is removed (AgriSageDbContext.ConfigureConventions); instead every
    // operational FK gets an index unless an unfiltered index already starts with its columns, and FKs to users
    // that only record an actor are not indexed (DECISIONS: actor FK indexes).
    private static void ApplyDeleteBehaviorAndForeignKeyIndexes(IMutableEntityType entityType)
    {
        foreach (var foreignKey in entityType.GetDeclaredForeignKeys().ToList())
        {
            foreignKey.DeleteBehavior = foreignKey.IsRequired ? DeleteBehavior.Restrict : DeleteBehavior.NoAction;

            var isActorReference = foreignKey.PrincipalEntityType.ClrType == typeof(User)
                && !OperationalUserForeignKeys.Contains(foreignKey.Properties[0].Name);

            var isCovered = entityType.GetIndexes().Any(index =>
                index.GetFilter() is null
                && index.Properties.Take(foreignKey.Properties.Count).SequenceEqual(foreignKey.Properties));

            if (!isActorReference && !isCovered)
            {
                entityType.AddIndex(foreignKey.Properties.ToList());
            }
        }
    }

    private static void ApplyConstraintNames(IMutableEntityType entityType)
    {
        var table = entityType.GetTableName()!;

        foreach (var key in entityType.GetDeclaredKeys())
        {
            key.SetName(key.IsPrimaryKey()
                ? Truncate($"pk_{table}")
                : Truncate($"ak_{table}_{Columns(key.Properties)}"));
        }

        foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
        {
            foreignKey.SetConstraintName(Truncate($"fk_{table}_{Columns(foreignKey.Properties)}"));
        }

        foreach (var index in entityType.GetDeclaredIndexes())
        {
            if (index.FindAnnotation(RelationalAnnotationNames.Name) is not null)
            {
                continue; // Explicitly named in the configuration (documented names).
            }

            var prefix = index.IsUnique ? "ux" : "ix";
            index.SetDatabaseName(Truncate($"{prefix}_{table}_{Columns(index.Properties)}"));
        }
    }

    private static string Columns(IEnumerable<IReadOnlyProperty> properties) =>
        string.Join("_", properties.Select(property => property.GetColumnName()));

    private static string Truncate(string identifier) =>
        identifier.Length <= MaxIdentifierLength ? identifier : identifier[..MaxIdentifierLength];

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}
