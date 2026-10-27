using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public sealed class PermissionOverrideInheritanceMigrationTests
{
    [Fact]
    public void Up_only_relaxes_member_override_nullability_and_preserves_existing_values()
    {
        var alter = Assert.IsType<AlterColumnOperation>(Assert.Single(new PermissionOverrideInheritance().UpOperations));
        Assert.Equal(("store_member_permissions", "is_granted", "boolean", true), (alter.Table, alter.Name, alter.ColumnType, alter.IsNullable));
        Assert.False(alter.OldColumn.IsNullable);
        Assert.Null(alter.DefaultValue);
        Assert.Null(alter.DefaultValueSql);
        using var context = new AgriSageDbContext(new DbContextOptionsBuilder<AgriSageDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.True(model.FindEntityType(typeof(StoreMemberPermission))!.FindProperty(nameof(StoreMemberPermission.IsGranted))!.IsNullable);
        Assert.False(model.FindEntityType(typeof(AgriSage.Domain.Features.Identity.Entities.RolePermission))!
            .FindProperty("IsGranted")!.IsNullable);
    }

    [Fact]
    public void Down_refuses_to_silently_replace_inheritance_with_an_explicit_deny()
    {
        var operations = new PermissionOverrideInheritance().DownOperations;
        Assert.Collection(operations,
            op => Assert.Contains("RAISE EXCEPTION", Assert.IsType<SqlOperation>(op).Sql),
            op => { var alter = Assert.IsType<AlterColumnOperation>(op); Assert.False(alter.IsNullable); Assert.Null(alter.DefaultValue); });
    }
}
