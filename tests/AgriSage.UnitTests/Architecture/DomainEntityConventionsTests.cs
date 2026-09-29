using System.Reflection;
using AgriSage.Domain.Common;

namespace AgriSage.UnitTests.Architecture;

// Guards coding rules for every Domain entity: no arbitrary public setters (§32),
// a parameterless constructor so EF Core can materialize the entity, and aggregate
// child entities that cannot be created or mutated around their aggregate root (#62).
public class DomainEntityConventionsTests
{
    private static readonly Type[] EntityTypes = typeof(BaseEntity).Assembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(BaseEntity)))
        .ToArray();

    private static readonly Type[] ChildEntityTypes = EntityTypes
        .Where(type => type.IsAssignableTo(typeof(SoftDeletableChildEntity)))
        .ToArray();

    [Fact]
    public void Child_entities_expose_no_public_methods_of_their_own()
    {
        var violations = ChildEntityTypes
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => $"{type.Name}.{method.Name}"));

        Assert.NotEmpty(ChildEntityTypes);
        Assert.Empty(violations);
    }

    [Fact]
    public void Child_entities_have_no_public_constructors()
    {
        var violations = ChildEntityTypes
            .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0)
            .Select(type => type.Name);

        Assert.Empty(violations);
    }

    [Fact]
    public void Domain_contains_entities()
    {
        Assert.NotEmpty(EntityTypes);
    }

    [Fact]
    public void Entities_have_no_public_setters()
    {
        var violations = EntityTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod?.IsPublic == true)
                .Select(property => $"{type.Name}.{property.Name}"));

        Assert.Empty(violations);
    }

    [Fact]
    public void Entities_have_a_parameterless_constructor_for_persistence()
    {
        var violations = EntityTypes
            .Where(type => type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                Type.EmptyTypes) is null)
            .Select(type => type.Name);

        Assert.Empty(violations);
    }
}
