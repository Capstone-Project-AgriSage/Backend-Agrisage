namespace AgriSage.Application.Features.Reports;

public sealed record CounterSalesTodayResponse(
    DateOnly Date,
    DateTimeOffset AsOf,
    decimal PaidSalesRevenue,
    int CompletedOrderCount,
    decimal ReceivedAmount,
    decimal CreditSalesAmount);

public interface ICounterSalesTodayService
{
    Task<CounterSalesTodayResponse> GetAsync(CancellationToken token);
}
