using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

// FLOW_2 §6 offline checks: roles, validation and the webhook answers that need no database (registration test 200,
// bad signature 400). payOS is the in-memory fake; the unconfigured real adapter answers 503.
public class PayOsHttpTests(PayOsHttpTests.PayOsApiFactory factory) : IClassFixture<PayOsHttpTests.PayOsApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Id = Guid.NewGuid().ToString();

    public sealed class PayOsApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(AgriSage.Api.Extensions.LocalSettingsExtensions.DisabledSetting, "true");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-a-secret-000000");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AgriSage.Application.Features.Permissions.IPermissionEvaluator>();
                services.AddScoped<AgriSage.Application.Features.Permissions.IPermissionEvaluator, BaselinePermissions>();
                services.RemoveAll<IUserAccessValidator>();
                services.AddSingleton<IUserAccessValidator>(new StaffHttpTests.FakeAccounts());
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(new FakePaymentGateway());
            });
        }
    }

    private HttpClient Client(string? role, WebApplicationFactory<Program>? app = null)
    {
        var client = (app ?? factory).CreateClient();
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", (app ?? factory).Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    [Fact]
    public async Task The_registration_test_is_200_and_a_bad_signature_400_without_sign_in()
    {
        using var client = Client(null);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/payments/payos/webhook", new StringContent("{}"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/payments/payos/webhook", new StringContent("{\"data\":{},\"signature\":\"x\"}"), Token)).StatusCode);
    }

    [Fact]
    public async Task Without_payos_configuration_a_signed_webhook_is_503()
    {
        await using var unconfigured = new StaffHttpTests.StaffApiFactory();
        using var client = unconfigured.CreateClient();
        var response = await client.PostAsync("/api/payments/payos/webhook",
            new StringContent("{\"code\":\"00\",\"success\":true,\"data\":{\"orderCode\":1},\"signature\":\"abc\"}"), Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public static TheoryData<string, string, string?> Gates => new()
    {
        // method, url, a role that must be refused
        { "POST", "/api/payments/payos", null }, { "POST", "/api/payments/payos", "FARMER" },
        { "POST", "/api/payments/payos", "DELIVERY_STAFF" }, { "POST", "/api/me/payments/payos", null },
        { "POST", "/api/me/payments/payos", "SALES_STAFF" }, { "POST", $"/api/me/payments/{Id}/cancel", "ADMIN" },
        { "POST", $"/api/payments/{Id}/sync", null }, { "POST", $"/api/payments/{Id}/sync", "DELIVERY_STAFF" },
        { "POST", $"/api/payments/{Id}/simulate-paid", null }, { "POST", $"/api/payments/{Id}/simulate-paid", "DELIVERY_STAFF" }
    };

    [Theory]
    [MemberData(nameof(Gates))]
    public async Task Roles_are_enforced(string method, string url, string? role)
    {
        using var client = Client(role);
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) }, Token);
        Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The test button does not exist unless the API runs with PayOS:Mode=Simulated: 404 for every role that may call it, before
    // anything is read from the database.
    [Theory]
    [InlineData("FARMER")]
    [InlineData("SALES_STAFF")]
    [InlineData("STORE_OWNER")]
    [InlineData("ADMIN")]
    public async Task The_test_button_is_404_when_payments_are_not_simulated(string role)
    {
        using var client = Client(role);

        var response = await client.PostAsync($"/api/payments/{Id}/simulate-paid", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Simulated_mode_swaps_the_gateway_in_development_and_the_real_one_stays_the_default()
    {
        await using var real = new StaffHttpTests.StaffApiFactory();
        Assert.Null(real.Services.GetService<ISimulatedPaymentGateway>());
        Assert.IsType<AgriSage.Infrastructure.Payments.PayOsPaymentGateway>(real.Services.GetRequiredService<IPaymentGateway>());

        await using var simulated = new StaffHttpTests.StaffApiFactory().WithWebHostBuilder(b => b.UseSetting("PayOS:Mode", "Simulated"));
        var simulator = simulated.Services.GetService<ISimulatedPaymentGateway>();
        Assert.NotNull(simulator);
        Assert.Same(simulator, simulated.Services.GetRequiredService<IPaymentGateway>());
    }

    [Theory]
    [InlineData("Production", "Simulated", "Development")]
    [InlineData("Staging", "simulated", "Development")]
    [InlineData("Development", "Fake", "Simulated")]
    public void The_api_refuses_to_start_with_simulated_payments_outside_development_or_with_an_unknown_mode(
        string environment, string mode, string expectedInMessage)
    {
        using var app = new StaffHttpTests.StaffApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("PayOS:Mode", mode);
        });

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(expectedInMessage, error.ToString());
    }

    public static TheoryData<object> InvalidRequests => new()
    {
        new { },
        new { paymentContext = "TIP" },
        new { paymentContext = "ORDER_PAYMENT" },
        new { paymentContext = "DEBT_REPAYMENT" },
        new { paymentContext = "DEBT_REPAYMENT", amount = 10_000, orderId = Guid.NewGuid() },
        new { paymentContext = "ORDER_PAYMENT", orderId = Guid.NewGuid(), amount = 10.001 },
        new { paymentContext = "ORDER_PAYMENT", orderId = Guid.NewGuid(), amount = -5 }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_link_requests_are_400(object body)
    {
        using var client = Client("FARMER");
        var response = await client.PostAsJsonAsync("/api/me/payments/payos", body, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Swagger_lists_the_payos_endpoints()
    {
        using var client = Client(null);
        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");
        foreach (var path in new[]
        {
            "/api/payments/payos", "/api/me/payments/payos", "/api/me/payments/{id}/cancel", "/api/payments/{id}/sync",
            "/api/payments/payos/webhook"
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
