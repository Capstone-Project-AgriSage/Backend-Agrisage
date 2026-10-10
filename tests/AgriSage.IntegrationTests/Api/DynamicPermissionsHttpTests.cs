using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AgriSage.Api.Authorization;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Permissions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;
public sealed class DynamicPermissionsHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [Fact]
    public void Every_business_action_has_a_known_permission_or_an_explicit_exception()
    {
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>();
        Assert.NotEmpty(actions);
        foreach (var action in actions.Where(a => !PermissionEndpointMap.IsException(a)))
        {
            var code = PermissionEndpointMap.CodeFor(action);
            Assert.True(code is not null && PermissionCatalog.ByCode.ContainsKey(code), $"Unmapped: {action.ControllerName}.{action.ActionName}");
        }
        Assert.Null(PermissionEndpointMap.CodeFor(new ControllerActionDescriptor { ControllerName = "Unknown", ActionName = "Create" }));
    }
    [Fact]
    public async Task Grant_and_revoke_take_effect_with_the_same_jwt_before_request_validation()
    {
        var grants = new ConcurrentDictionary<string, bool>();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPermissionEvaluator>();
            services.AddSingleton<IPermissionEvaluator>(new LivePermissions(grants));
        }));
        using var client = app.CreateClient();
        var jwt = app.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), "SALES_STAFF");
        client.DefaultRequestHeaders.Authorization = new("Bearer", jwt.Value);
        async Task<HttpStatusCode> Send() => (await client.PostAsJsonAsync("/api/products", new { }, TestContext.Current.CancellationToken)).StatusCode;
        Assert.Equal(HttpStatusCode.Forbidden, await Send());
        grants["PRODUCTS.CREATE"] = true;
        Assert.Equal(HttpStatusCode.BadRequest, await Send()); // reached validation, no database or mutation
        grants["PRODUCTS.CREATE"] = false;
        Assert.Equal(HttpStatusCode.Forbidden, await Send());
    }
    [Fact]
    public void Configuration_validators_reject_unknown_duplicate_null_and_missing_reason()
    {
        var roles = new SetRolePermissionsRequestValidator();
        Assert.False(roles.Validate(new SetRolePermissionsRequest(["UNKNOWN"], 0, "test")).IsValid);
        Assert.False(roles.Validate(new SetRolePermissionsRequest(["PRODUCTS.READ", "PRODUCTS.READ"], 0, "test")).IsValid);
        Assert.False(roles.Validate(new SetRolePermissionsRequest([], 0, " ")).IsValid);
        var members = new SetMemberPermissionsRequestValidator();
        Assert.False(members.Validate(new SetMemberPermissionsRequest([new("UNKNOWN", true)], 0, 0, "test")).IsValid);
        Assert.False(members.Validate(new SetMemberPermissionsRequest([null!], 0, 0, "test")).IsValid);
        Assert.False(members.Validate(new SetMemberPermissionsRequest([new("ORDERS.READ", true), new("ORDERS.READ", false)], 0, 0, "test")).IsValid);
        Assert.True(members.Validate(new SetMemberPermissionsRequest([new("ORDERS.READ", false)], 0, 0, "test")).IsValid);
        Assert.False(PermissionCatalog.ByCode["PERMISSIONS.MANAGE_ROLES"].AllowsRole("SALES_STAFF"));
        Assert.False(PermissionCatalog.ByCode["AUDIT.READ"].Delegable);
    }
    private sealed class LivePermissions(ConcurrentDictionary<string, bool> grants) : IPermissionEvaluator
    {
        public Task<bool> HasAsync(string code, CancellationToken token) => Task.FromResult(grants.GetValueOrDefault(code));
        public Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token) => Task.FromResult(new CurrentPermissionsResponse("SALES_STAFF", null, 0, 0, grants.Where(p => p.Value).Select(p => p.Key).ToList()));
    }
}
