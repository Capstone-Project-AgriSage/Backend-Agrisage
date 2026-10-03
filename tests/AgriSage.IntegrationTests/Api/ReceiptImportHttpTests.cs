using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Goods receipt Excel import endpoints (F4.3) without a database: roles, form and file validation, Swagger.
public class ReceiptImportHttpTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public ReceiptImportHttpTests(StaffHttpTests.StaffApiFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string? role)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            var token = _factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.Value);
        }

        return client;
    }

    private static MultipartFormDataContent Form(Guid? supplierId, string? fileName = null, byte[]? content = null)
    {
        var form = new MultipartFormDataContent();
        if (supplierId is not null)
        {
            form.Add(new StringContent(supplierId.Value.ToString()), "supplierId");
        }

        if (fileName is not null)
        {
            var file = new ByteArrayContent(content ?? [1, 2, 3]);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
        }

        return form;
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("errors");

    private async Task<HttpStatusCode> SendAsync(HttpClient client, string what) => what switch
    {
        "template" => (await client.GetAsync("/api/goods-receipts/import-template", Token)).StatusCode,
        "preview" => (await client.PostAsync("/api/goods-receipts/import/preview", Form(Guid.NewGuid(), "a.xlsx"), Token)).StatusCode,
        _ => (await client.PostAsync("/api/goods-receipts/import", Form(Guid.NewGuid(), "a.xlsx"), Token)).StatusCode
    };

    [Theory]
    [InlineData("template")]
    [InlineData("preview")]
    [InlineData("import")]
    public async Task Import_endpoints_need_a_token_and_are_closed_to_delivery_staff_and_farmers(string what)
    {
        using (var anonymous = ClientFor(null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(anonymous, what));
        }

        foreach (var role in new[] { "DELIVERY_STAFF", "FARMER" })
        {
            using var client = ClientFor(role);
            Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(client, what));
        }
    }

    // The upload rate limit (30/min per client) is shared by every request of this class: keep the count low.
    [Theory]
    [InlineData("ADMIN", "/api/goods-receipts/import/preview")]
    [InlineData("STORE_OWNER", "/api/goods-receipts/import/preview")]
    [InlineData("SALES_STAFF", "/api/goods-receipts/import/preview")]
    [InlineData("SALES_STAFF", "/api/goods-receipts/import")]
    public async Task Staff_reach_validation_a_missing_supplier_or_file_is_400(string role, string url)
    {
        using var client = ClientFor(role);

        var noSupplier = await client.PostAsync(url, Form(null, "a.xlsx"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, noSupplier.StatusCode);
        Assert.True((await ErrorsAsync(noSupplier)).TryGetProperty("supplierId", out _));

        // The file is checked before any database access.
        var noFile = await client.PostAsync(url, Form(Guid.NewGuid()), Token);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        Assert.True((await ErrorsAsync(noFile)).TryGetProperty("file", out _));
    }

    [Theory]
    [InlineData("receipt.csv")]
    [InlineData("receipt.xls")]
    [InlineData("receipt.xlsx")]
    public async Task Only_real_xlsx_workbooks_are_read(string fileName)
    {
        using var client = ClientFor("SALES_STAFF");

        var response = await client.PostAsync(
            "/api/goods-receipts/import/preview", Form(Guid.NewGuid(), fileName, "SKU,Packaging\nA,B"u8.ToArray()), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var file = (await ErrorsAsync(response)).GetProperty("file")[0].GetString();
        Assert.Contains(fileName.EndsWith(".xlsx") ? "not a valid .xlsx" : "Only .xlsx", file);
    }

    [Fact]
    public async Task A_file_over_two_megabytes_is_refused()
    {
        using var client = ClientFor("SALES_STAFF");

        var response = await client.PostAsync(
            "/api/goods-receipts/import/preview", Form(Guid.NewGuid(), "big.xlsx", new byte[2 * 1024 * 1024 + 1]), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("2 MB", (await ErrorsAsync(response)).GetProperty("file")[0].GetString());
    }

    [Fact]
    public async Task Swagger_lists_the_import_endpoints()
    {
        using var client = ClientFor(null);

        var paths = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/goods-receipts/import-template", "/api/goods-receipts/import/preview", "/api/goods-receipts/import"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }
    }
}
