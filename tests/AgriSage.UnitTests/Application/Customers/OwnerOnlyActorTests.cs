using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Permissions;

namespace AgriSage.UnitTests.Application.Customers;

// F3.21: the owner-only role gate must hold with or without the dynamic-permission evaluator, and a granted permission
// never lets Sale staff through. The guard touches neither the database nor the audit trail, so none is built.
public class OwnerOnlyActorTests
{
    private sealed class User(string? role, Guid? id) : ICurrentUserService
    {
        public Guid? UserId { get; } = id;
        public bool IsAuthenticated => UserId is not null;
        public string? Role { get; } = role;
    }

    private sealed class Grants(params string[] granted) : IPermissionEvaluator
    {
        public Task<bool> HasAsync(string code, CancellationToken token) => Task.FromResult(granted.Contains(code));
        public Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token) =>
            Task.FromResult(new CurrentPermissionsResponse("", null, 0, 0, granted));
    }

    private static CustomerWrites Writes(string? role, Guid? id, IPermissionEvaluator? evaluator) =>
        new(null!, new User(role, id), null!, null!, evaluator);

    private const string Code = "DEBT.ADJUST";
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sale_staff_is_refused_even_with_the_permission_granted(bool withEvaluator)
    {
        var writes = Writes("SALES_STAFF", Guid.NewGuid(), withEvaluator ? new Grants(Code) : null);

        await Assert.ThrowsAsync<ForbiddenException>(() => writes.OwnerActorAsync(Code, Token));
    }

    [Theory]
    [InlineData("DELIVERY_STAFF")]
    [InlineData("FARMER")]
    [InlineData(null)]
    public async Task Other_roles_are_refused(string? role)
    {
        var writes = Writes(role, Guid.NewGuid(), new Grants(Code));

        await Assert.ThrowsAsync<ForbiddenException>(() => writes.OwnerActorAsync(Code, Token));
    }

    [Theory]
    [InlineData("STORE_OWNER")]
    [InlineData("ADMIN")]
    public async Task Owner_and_admin_pass_with_or_without_the_evaluator(string role)
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, await Writes(role, id, new Grants(Code)).OwnerActorAsync(Code, Token));
        Assert.Equal(id, await Writes(role, id, null).OwnerActorAsync(Code, Token));
    }

    [Fact]
    public async Task Owner_without_the_permission_is_refused_when_the_evaluator_is_present()
    {
        var writes = Writes("STORE_OWNER", Guid.NewGuid(), new Grants());

        await Assert.ThrowsAsync<ForbiddenException>(() => writes.OwnerActorAsync(Code, Token));
    }

    [Fact]
    public async Task Unauthenticated_caller_is_refused()
    {
        var writes = Writes("STORE_OWNER", null, new Grants(Code));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => writes.OwnerActorAsync(Code, Token));
    }
}
