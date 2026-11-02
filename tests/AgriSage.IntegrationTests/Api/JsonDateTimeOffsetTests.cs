using System.Text.Json;
using AgriSage.Application.Features.Pricing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Api;

// A request date-time with a non-zero offset ("...+07:00") used to reach the database as is: Npgsql refuses it for timestamptz and the
// caller got HTTP 500. The API now normalises every incoming DateTimeOffset to UTC (the same instant). No database is needed: the
// check reads the JSON options the controllers really use.
public sealed class JsonDateTimeOffsetTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private JsonSerializerOptions ApiOptions => factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

    [Fact]
    public void A_request_date_time_with_an_offset_arrives_as_the_same_instant_in_utc()
    {
        var request = JsonSerializer.Deserialize<PriceListRequest>(
            """{"code":"X","name":"Y","effectiveFrom":"2026-01-01T00:00:00+07:00","effectiveTo":"2026-12-31T23:59:59-05:00"}""",
            ApiOptions)!;

        Assert.Equal(TimeSpan.Zero, request.EffectiveFrom.Offset);
        Assert.Equal(new DateTimeOffset(2025, 12, 31, 17, 0, 0, TimeSpan.Zero), request.EffectiveFrom);
        Assert.Equal(TimeSpan.Zero, request.EffectiveTo!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 4, 59, 59, TimeSpan.Zero), request.EffectiveTo.Value);
    }

    [Fact]
    public void A_date_time_that_is_already_utc_and_a_missing_one_are_unchanged()
    {
        var request = JsonSerializer.Deserialize<PriceListRequest>(
            """{"code":"X","name":"Y","effectiveFrom":"2026-01-01T00:00:00Z"}""", ApiOptions)!;

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), request.EffectiveFrom);
        Assert.Null(request.EffectiveTo);
    }

    [Fact]
    public void Responses_are_written_the_way_they_were_before()
    {
        var value = new DateTimeOffset(2026, 1, 1, 10, 30, 0, TimeSpan.Zero);

        Assert.Equal(JsonSerializer.Serialize(value, new JsonSerializerOptions()), JsonSerializer.Serialize(value, ApiOptions));
    }
}
