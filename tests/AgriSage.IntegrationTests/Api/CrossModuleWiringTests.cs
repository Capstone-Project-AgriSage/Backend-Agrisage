using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// Every cross-module interface of the API contracts resolves from the real API container (temporary or real
// implementation), so a task that consumes one never has to register it itself.
public class CrossModuleWiringTests : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private readonly StaffHttpTests.StaffApiFactory _factory;

    public CrossModuleWiringTests(StaffHttpTests.StaffApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<Type> Interfaces =>
    [
        typeof(IPriceResolver),
        typeof(IOrderSettlementGuard),
        typeof(ICreditReservationAdjuster),
        typeof(IFulfillmentFinancialPosting),
        typeof(IDebtRepaymentPosting),
        typeof(IDebtReturnPosting),
        typeof(IOrderPrepaymentLedger),
        typeof(IOrderPaymentCancellation)
    ];

    [Theory]
    [MemberData(nameof(Interfaces))]
    public void The_interface_resolves_exactly_once(Type contract)
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(contract));
        Assert.Single(scope.ServiceProvider.GetServices(contract));
    }
}
