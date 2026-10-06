namespace AgriSage.Application.Common.Interfaces;

// Extra of the simulated payment gateway (PayOS:Mode = Simulated), for the test environment only: lets a tester "pay" a link
// without payOS. It is registered in that mode alone; the payOS adapter does not implement it, so production code that asks
// for it gets null.
public interface ISimulatedPaymentGateway
{
    // Marks a PENDING simulated link paid in full. False when the order code is unknown (for example the API restarted
    // since the link was made) or the link is no longer pending.
    bool TryMarkPaid(long orderCode);
}
