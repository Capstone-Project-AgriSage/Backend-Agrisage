using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Entities;

namespace AgriSage.Domain.Features.Exchanges.Entities;

public enum ExchangeStatus { Requested, Approved, Received, ReadyToComplete, Completed, Rejected, Cancelled }

public sealed class SalesExchange : SoftDeletableEntity
{
    private readonly List<ExchangeItem> _items = [];
    private SalesExchange() { }
    public SalesExchange(SalesReturn salesReturn, Guid requestKey, string requestHash, Guid requestedBy, string reason)
    {
        if (requestKey == Guid.Empty) throw new DomainException("An exchange needs an idempotency key.");
        StoreId = salesReturn.StoreId; OrderId = salesReturn.OrderId; SalesReturnId = salesReturn.Id;
        FarmerProfileId = salesReturn.FarmerProfileId; RequestKey = requestKey;
        RequestHash = Guard.NotNullOrWhiteSpace(requestHash); RequestedBy = requestedBy;
        Reason = Guard.NotNullOrWhiteSpace(reason);
    }
    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid SalesReturnId { get; private set; }
    public Guid? FarmerProfileId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string RequestHash { get; private set; } = null!;
    public Guid RequestedBy { get; private set; }
    public string Reason { get; private set; } = null!;
    public ExchangeStatus Status { get; private set; }
    public decimal ReplacementAmount { get; private set; }
    public decimal? Difference { get; private set; }
    public Guid? ReplacementOrderId { get; private set; }
    public string? SettlementReference { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public IReadOnlyCollection<ExchangeItem> Items => _items.AsReadOnly();
    public void AddReplacement(OrderItem item)
    {
        Ensure(ExchangeStatus.Requested);
        _items.Add(new ExchangeItem(Id, item));
        ReplacementAmount = _items.Sum(i => i.LineTotal);
    }
    public void Approve() { Ensure(ExchangeStatus.Requested); if (_items.Count == 0) throw new DomainException("Select replacement goods first."); Status = ExchangeStatus.Approved; }
    public void Reject() { Ensure(ExchangeStatus.Requested); Status = ExchangeStatus.Rejected; }
    public void Cancel() { Ensure(ExchangeStatus.Requested, ExchangeStatus.Approved); Status = ExchangeStatus.Cancelled; }
    public void Receive() { Ensure(ExchangeStatus.Approved); Status = ExchangeStatus.Received; }
    public void FinishInspection(decimal credit)
    {
        Ensure(ExchangeStatus.Received); Difference = ReplacementAmount - Guard.NonNegativeMoney(credit);
        Status = ExchangeStatus.ReadyToComplete;
    }
    public void Complete(Order replacement, decimal confirmedDifference, string reference, DateTimeOffset at)
    {
        Ensure(ExchangeStatus.ReadyToComplete);
        if (replacement.Id == OrderId || replacement.Status != AgriSage.Domain.Features.Orders.Enums.OrderStatus.Completed || replacement.StoreId != StoreId
            || replacement.FarmerProfileId != FarmerProfileId || replacement.TotalAmount != ReplacementAmount || Difference != confirmedDifference)
            throw new DomainException("The completed replacement and confirmed settlement must match this exchange.");
        SettlementReference = Guard.NotNullOrWhiteSpace(reference);
        ReplacementOrderId = replacement.Id; CompletedAt = at; Status = ExchangeStatus.Completed;
    }
    protected override void EnsureCanBeDeleted() => throw new DomainException("Exchange history must be preserved; cancel the request instead.");

    private void Ensure(params ExchangeStatus[] allowed)
    {
        if (!allowed.Contains(Status)) throw new DomainException($"Exchange is {Status}; this action is not allowed.");
    }
}
