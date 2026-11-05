using AgriSage.Application.Common;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Debt.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Debt;

public sealed record DebtEntryListRequest : PaginationRequest
{
    public Guid? FarmerProfileId { get; init; }
    public Guid? OrderId { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public bool OverdueOnly { get; init; }
    public DateOnly? DueFrom { get; init; }
    public DateOnly? DueTo { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string SortBy { get; init; } = "CreatedAt";
    public bool Descending { get; init; } = true;
    // F3.17: seasons (D9), due state, rolled-over entries and "Cần quyết định".
    public Guid? OriginSeasonId { get; init; }
    public Guid? CollectionSeasonId { get; init; }
    public string? DueState { get; init; }
    public bool RolledOverOnly { get; init; }
    public bool NeedsDecision { get; init; }
}
public sealed record DebtAccountListRequest : PaginationRequest
{
    public string? Search { get; init; }
    public bool? HasOutstanding { get; init; }
    public bool OverdueOnly { get; init; }
}
public sealed record DebtTransactionListRequest : PaginationRequest
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}
public sealed record DebtReasonRequest(string Reason);
// ReasonCode (D7, CS-11, A1): required, one of the 7 codes.
public sealed record DebtAdjustmentRequest(decimal Amount, string Reason, string? ReasonCode = null);
public sealed record DebtCancelRequest(string Reason, string? ReasonCode = null);
public sealed record DebtDueDateRequest(DateOnly NewDueDate, string Reason, string? ReasonCode = null);
// F3.13: with CropSeasonId the debt belongs to that season (origin = collection) and DueDate may be left out: it then takes
// the season's due date. DueDate stays required without a season.
public sealed record ManualDebtEntryRequest(decimal Amount, DateOnly? DueDate, string Reason, Guid? CropSeasonId = null);
public sealed record DebtAccountResponse(Guid Id, Guid FarmerProfileId, string Status, decimal CurrentBalance,
    decimal OverdueAmount, int OpenEntryCount, DateOnly? OldestDueDate, DateTimeOffset? LastTransactionAt, long Version,
    IReadOnlyList<DebtCollectionGroup> Groups);
// F3.17: a season of the store as shown on a debt (D16).
public sealed record DebtSeasonReference(Guid Id, string Code, string Name);
// Open debt of one customer collected in one season with one due date (no season: the credit-tier term), as reminded (D10).
public sealed record DebtCollectionGroup(DebtSeasonReference? CollectionSeason, DateOnly DueDate, decimal OutstandingAmount,
    int EntryCount, string DueState);
// A customer whose used credit (outstanding debt + active reservations) reaches 90 % of the limit (NEAR_LIMIT) or all of it
// (AT_LIMIT): the owner decides to raise the limit, reduce the debt or collect part of it.
public sealed record CreditLimitAlert(Guid FarmerProfileId, string FullName, string? PhoneNumber, decimal CreditLimit,
    decimal UsedCredit, decimal AvailableCredit, string State);
public sealed record DebtAccountListItem(Guid Id, Guid FarmerProfileId, string FullName, string? PhoneNumber,
    CustomerReference? CustomerGroup, decimal CurrentBalance, decimal OverdueAmount, DateOnly? OldestDueDate);
public sealed record DebtEntryListItem(Guid Id, string EntryNumber, Guid FarmerProfileId, string FullName,
    string? PhoneNumber, string SourceType, string? OrderNumber, decimal OriginalAmount, decimal TotalPaid,
    decimal OutstandingAmount, DateOnly DueDate, bool IsOverdue, int OverdueDays, string Status, DateTimeOffset CreatedAt,
    DebtSeasonReference? OriginSeason, DebtSeasonReference? CollectionSeason, DateOnly OriginalDueDate, int RolloverCount,
    bool IsRolledOver, string? DueState, bool NeedsDecision);
public sealed record DebtTransactionResponse(Guid Id, Guid? DebtEntryId, string TransactionType, decimal AmountDelta,
    decimal BalanceBefore, decimal BalanceAfter, DateTimeOffset OccurredAt, string Status, Guid? PaymentAllocationId,
    Guid? SalesReturnId, Guid? DebtEntryActionId, string? Note, Guid? CreatedBy);
// IsPrePolicy: a debt change (adjust, cancel, due date, rollover) recorded without a reason code (D7).
public sealed record DebtActionResponse(Guid Id, string ActionType, string Reason, decimal? AdjustmentAmount,
    DateOnly? OldDueDate, DateOnly? NewDueDate, Guid CreatedBy, DateTimeOffset CreatedAt, string? ReasonCode = null,
    bool IsPrePolicy = false, Guid? OldCropSeasonId = null, Guid? NewCropSeasonId = null, string? ActorName = null,
    string? ActorRole = null);
public sealed record DebtCustomerResponse(Guid Id, string FullName, string? PhoneNumber, decimal CreditLimit,
    decimal CurrentOutstandingDebt, decimal ReservedCredit, decimal AvailableCredit);
public sealed record DebtEntryResponse(Guid Id, string EntryNumber, string SourceType, Guid? OrderId, string? OrderNumber,
    Guid? DeliveryId, Guid? DeliveryAttemptId, Guid? SourceStockMovementId, decimal FulfillmentValue,
    decimal PrepaymentAppliedAmount, decimal OriginalAmount, decimal TotalPaid, decimal OutstandingAmount,
    DateOnly DueDate, bool IsOverdue, int OverdueDays, string Status, DebtCustomerResponse Customer,
    OrderResponse? Order, IReadOnlyList<DebtActionResponse> Actions, IReadOnlyList<DebtTransactionResponse> Transactions,
    IReadOnlyList<PaymentResponse> Payments, DateTimeOffset CreatedAt, DebtSeasonReference? OriginSeason,
    DebtSeasonReference? CollectionSeason, DateOnly OriginalDueDate, int RolloverCount, bool IsRolledOver, string? DueState,
    bool NeedsDecision);
public sealed record AllocationPreviewItem(Guid DebtEntryId, string EntryNumber, DateOnly DueDate,
    decimal OutstandingAmount, decimal AllocatedAmount);
public sealed record AllocationPreviewResponse(decimal Amount, IReadOnlyList<AllocationPreviewItem> Allocations, decimal UnallocatedAmount);
public sealed record DebtDashboardResponse(decimal TotalOutstandingDebt, decimal TotalOverdueDebt, decimal CollectedToday,
    decimal CollectedThisMonth, int CustomersWithDebt, int CustomersWithOverdueDebt,
    IReadOnlyList<DebtAccountListItem> TopDebtors, IReadOnlyList<PaymentListItem> RecentDebtPayments,
    IReadOnlyList<DebtEntryListItem> OverdueDebts, DebtSeasonReference? CurrentSeason, decimal CurrentSeasonOutstanding,
    decimal DueSoonAmount, int NeedsDecisionCount, IReadOnlyList<DebtEntryListItem> NeedsDecision, int CreditLimitAlertCount,
    IReadOnlyList<CreditLimitAlert> CreditLimitAlerts);

public interface IDebtService
{
    Task<PagedResult<DebtEntryListItem>> EntriesAsync(DebtEntryListRequest request, CancellationToken token);
    Task<DebtEntryResponse> EntryAsync(Guid id, CancellationToken token);
    Task<PagedResult<DebtAccountListItem>> AccountsAsync(DebtAccountListRequest request, CancellationToken token);
    Task<DebtAccountResponse> AccountAsync(Guid farmerId, CancellationToken token);
    Task<PagedResult<DebtTransactionResponse>> TransactionsAsync(Guid farmerId, DebtTransactionListRequest request, CancellationToken token);
    Task<AllocationPreviewResponse> PreviewAsync(Guid farmerId, decimal amount, CancellationToken token);
    Task<DebtEntryResponse> ActionAsync(Guid id, string action, string reason, decimal? amount, DateOnly? dueDate, CancellationToken token);
    Task<DebtEntryResponse> ActionAsync(Guid id, string action, string reason, decimal? amount, DateOnly? dueDate, string? reasonCode, CancellationToken token);
    Task<DebtEntryResponse> ManualAsync(Guid farmerId, ManualDebtEntryRequest request, CancellationToken token);
    Task<DebtDashboardResponse> DashboardAsync(CancellationToken token);
    Task<Guid> OwnCustomerAsync(CancellationToken token);
    Task RequireOwnedEntryAsync(Guid entryId, Guid farmerId, CancellationToken token);
    Task<DebtEntryResponse> DisputeOwnAsync(Guid id, string reason, CancellationToken token);
}
public sealed class DebtEntryListRequestValidator : AbstractValidator<DebtEntryListRequest>
{
    public DebtEntryListRequestValidator()
    {
        Include(new Common.Validators.PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<DebtEntryStatus>(s, out _)).When(r => !string.IsNullOrWhiteSpace(r.Status));
        RuleFor(r => r.SortBy).Must(s => s != null && new[] { "CREATEDAT", "DUEDATE", "OUTSTANDINGAMOUNT", "DAYSOVERDUE" }.Contains(s.ToUpperInvariant()));
        RuleFor(r => r.Search).MaximumLength(200);
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate).When(r => r.FromDate != null && r.ToDate != null);
        RuleFor(r => r.DueTo).GreaterThanOrEqualTo(r => r.DueFrom).When(r => r.DueFrom != null && r.DueTo != null);
        RuleFor(r => r.DueState).Must(s => s is "NOT_DUE" or "DUE_SOON" or "OVERDUE").When(r => r.DueState is not null)
            .WithMessage("Unknown due state. Use NOT_DUE, DUE_SOON or OVERDUE.");
        RuleFor(r => r.OriginSeasonId).NotEqual(Guid.Empty).When(r => r.OriginSeasonId.HasValue);
        RuleFor(r => r.CollectionSeasonId).NotEqual(Guid.Empty).When(r => r.CollectionSeasonId.HasValue);
    }
}
public sealed class DebtAccountListRequestValidator : AbstractValidator<DebtAccountListRequest>
{
    public DebtAccountListRequestValidator() { Include(new Common.Validators.PaginationRequestValidator()); RuleFor(r => r.Search).MaximumLength(200); }
}
public sealed class DebtTransactionListRequestValidator : AbstractValidator<DebtTransactionListRequest>
{
    public DebtTransactionListRequestValidator()
    {
        Include(new Common.Validators.PaginationRequestValidator());
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate).When(r => r.FromDate != null && r.ToDate != null);
    }
}
public sealed class DebtReasonRequestValidator : AbstractValidator<DebtReasonRequest>
{
    public DebtReasonRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}
public sealed class DebtAdjustmentRequestValidator : AbstractValidator<DebtAdjustmentRequest>
{
    public DebtAdjustmentRequestValidator()
    {
        RuleFor(r => r.Amount).Must(a => a > 0 && Credit.CreditMoney.Valid(a));
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.ReasonCode).NotEmpty().Must(DebtReasonCodeText.IsKnown).WithMessage(DebtReasonCodeText.Unknown);
    }
}
public sealed class ManualDebtEntryRequestValidator : AbstractValidator<ManualDebtEntryRequest>
{
    public ManualDebtEntryRequestValidator()
    {
        RuleFor(r => r.Amount).Must(a => a > 0 && Credit.CreditMoney.Valid(a));
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.DueDate).NotNull().When(r => r.CropSeasonId is null);
        RuleFor(r => r.DueDate!.Value).NotEqual(default(DateOnly)).When(r => r.DueDate.HasValue).OverridePropertyName("dueDate");
        RuleFor(r => r.CropSeasonId!.Value).NotEqual(Guid.Empty).When(r => r.CropSeasonId.HasValue).OverridePropertyName("cropSeasonId");
    }
}
public sealed class DebtDueDateRequestValidator : AbstractValidator<DebtDueDateRequest>
{
    public DebtDueDateRequestValidator()
    {
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000); RuleFor(r => r.NewDueDate).NotEmpty();
        RuleFor(r => r.ReasonCode).NotEmpty().Must(DebtReasonCodeText.IsKnown).WithMessage(DebtReasonCodeText.Unknown);
    }
}
public sealed class DebtCancelRequestValidator : AbstractValidator<DebtCancelRequest>
{
    public DebtCancelRequestValidator()
    {
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.ReasonCode).NotEmpty().Must(DebtReasonCodeText.IsKnown).WithMessage(DebtReasonCodeText.Unknown);
    }
}

// The 7 reason codes as UPPER_SNAKE text (MAT_MUA, THIEN_TAI, ...), as stored and returned.
public static class DebtReasonCodeText
{
    public const string Unknown = "Unknown reason code. Use MAT_MUA, THIEN_TAI, KHO_KHAN, TRI_AN, SAI_SOT, KHONG_THU_HOI or KHAC.";

    public static bool IsKnown(string? text) => EnumText.TryParse<DebtReasonCode>(text, out _);

    // A change needs a code: a missing or unknown one is refused (the request validators refuse it first with 400).
    public static DebtReasonCode Require(string? text) => Parse(text) ?? throw new Common.Exceptions.BusinessRuleException("A reason code is required. " + Unknown);

    // Null when no code was sent; an unknown code is refused.
    public static DebtReasonCode? Parse(string? text) => text is null ? null
        : EnumText.TryParse<DebtReasonCode>(text, out var code) ? code
        : throw new Common.Exceptions.BusinessRuleException(Unknown);
}
