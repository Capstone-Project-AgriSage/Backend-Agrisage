using AgriSage.Application.Common;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Returns;
using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Features.Exchanges.Entities;
using FluentValidation;

namespace AgriSage.Application.Features.Exchanges;

public sealed record ReplacementRequest(Guid StoreProductId, Guid ProductPackagingId, long Quantity);
public sealed record CreateExchangeRequest(Guid OrderId, Guid RequestKey, string Reason,
    IReadOnlyList<ReturnItemRequest> Items, IReadOnlyList<ReplacementRequest> Replacements);
public sealed record ExchangeLotRequest(Guid ReplacementItemId, Guid InventoryLotId, long BaseQuantity);
public sealed record CompleteExchangeRequest(decimal ConfirmedDifference, string SettlementReference, IReadOnlyList<ExchangeLotRequest> Lots);
public sealed record ExchangeListRequest : PaginationRequest
{
    public Guid? OrderId { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
}
public sealed record ExchangeLineResponse(Guid Id, Guid StoreProductId, Guid ProductPackagingId, string ProductName,
    string PackagingName, long Quantity, long BaseQuantity, decimal UnitPrice, decimal LineTotal);
public sealed record ExchangeOriginalItemResponse(Guid OrderItemId, string ProductName, string PackagingName);
public sealed record ExchangeHistoryResponse(string Action, DateTimeOffset OccurredAt, Guid? ActorId, string? Reason);
public sealed record ExchangeListItem(Guid Id, Guid OrderId, string OrderNumber, string ReturnNumber, string CustomerName,
    string Status, decimal ReplacementAmount, decimal? Difference, DateTimeOffset CreatedAt);
public sealed record ExchangeSummaryResponse(decimal ReceivedAmount, decimal RefundedAmount, decimal NetReceivedAmount);
public sealed record ExchangeListResponse(IReadOnlyList<ExchangeListItem> Items, int Page, int PageSize,
    long TotalCount, ExchangeSummaryResponse Summary)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)((TotalCount + PageSize - 1) / PageSize);
}
public sealed record ExchangeResponse(Guid Id, Guid OrderId, string OrderNumber, string Status, string Reason,
    decimal ReplacementAmount, decimal? Difference, Guid? ReplacementOrderId, string? ReplacementOrderNumber, string? SettlementReference,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, SalesReturnResponse Return,
    IReadOnlyList<ExchangeOriginalItemResponse> OriginalItems, IReadOnlyList<ExchangeLineResponse> Replacements, IReadOnlyList<ExchangeHistoryResponse> History);

public sealed class CreateExchangeRequestValidator : AbstractValidator<CreateExchangeRequest>
{
    public CreateExchangeRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty(); RuleFor(x => x.RequestKey).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Items).NotEmpty().Must(x => x == null || x.Count <= 100);
        RuleForEach(x => x.Items).NotNull().SetValidator(new ReturnItemRequestValidator());
        RuleFor(x => x.Replacements).NotEmpty().Must(x => x == null || x.Count <= 100)
            .Must(x => x == null || x.All(i => i != null) && x.Select(i => (i.StoreProductId, i.ProductPackagingId)).Distinct().Count() == x.Count);
        RuleForEach(x => x.Replacements).NotNull().ChildRules(i =>
        {
            i.RuleFor(x => x.StoreProductId).NotEmpty(); i.RuleFor(x => x.ProductPackagingId).NotEmpty();
            i.RuleFor(x => x.Quantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity);
        });
    }
}
public sealed class CompleteExchangeRequestValidator : AbstractValidator<CompleteExchangeRequest>
{
    public CompleteExchangeRequestValidator()
    {
        RuleFor(x => x.ConfirmedDifference).Must(v => decimal.Round(v, 2) == v && v >= -9999999999999999.99m && v <= 9999999999999999.99m);
        RuleFor(x => x.SettlementReference).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Lots).NotEmpty().Must(x => x == null || x.Count <= 1000)
            .Must(x => x == null || x.All(i => i != null) && x.Select(i => (i.ReplacementItemId, i.InventoryLotId)).Distinct().Count() == x.Count);
        RuleForEach(x => x.Lots).NotNull().ChildRules(i =>
        {
            i.RuleFor(x => x.ReplacementItemId).NotEmpty(); i.RuleFor(x => x.InventoryLotId).NotEmpty();
            i.RuleFor(x => x.BaseQuantity).GreaterThan(0);
        });
    }
}
public sealed class ExchangeListRequestValidator : AbstractValidator<ExchangeListRequest>
{
    public ExchangeListRequestValidator()
    {
        Include(new PaginationRequestValidator()); RuleFor(x => x.OrderId).NotEqual(Guid.Empty);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).Must(v => EnumText.TryParse<ExchangeStatus>(v, out _)).When(x => !string.IsNullOrEmpty(x.Status));
    }
}
