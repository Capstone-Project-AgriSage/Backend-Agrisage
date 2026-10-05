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
public sealed record DebtAdjustmentRequest(decimal Amount, string Reason);
public sealed record DebtDueDateRequest(DateOnly NewDueDate, string Reason);
public sealed record ManualDebtEntryRequest(decimal Amount, DateOnly DueDate, string Reason);
public sealed record DebtAccountResponse(Guid Id, Guid FarmerProfileId, string Status, decimal CurrentBalance,
    decimal OverdueAmount, int OpenEntryCount, DateOnly? OldestDueDate, DateTimeOffset? LastTransactionAt, long Version);
public sealed record DebtAccountListItem(Guid Id, Guid FarmerProfileId, string FullName, string? PhoneNumber,
    CustomerReference? CustomerGroup, decimal CurrentBalance, decimal OverdueAmount, DateOnly? OldestDueDate);
public sealed record DebtEntryListItem(Guid Id, string EntryNumber, Guid FarmerProfileId, string FullName,
    string? PhoneNumber, string SourceType, string? OrderNumber, decimal OriginalAmount, decimal TotalPaid,
    decimal OutstandingAmount, DateOnly DueDate, bool IsOverdue, int OverdueDays, string Status, DateTimeOffset CreatedAt);
public sealed record DebtTransactionResponse(Guid Id, Guid? DebtEntryId, string TransactionType, decimal AmountDelta,
    decimal BalanceBefore, decimal BalanceAfter, DateTimeOffset OccurredAt, string Status, Guid? PaymentAllocationId,
    Guid? SalesReturnId, Guid? DebtEntryActionId, string? Note, Guid? CreatedBy);
public sealed record DebtActionResponse(Guid Id, string ActionType, string Reason, decimal? AdjustmentAmount,
    DateOnly? OldDueDate, DateOnly? NewDueDate, Guid CreatedBy, DateTimeOffset CreatedAt);
public sealed record DebtCustomerResponse(Guid Id, string FullName, string? PhoneNumber, decimal CreditLimit,
    decimal CurrentOutstandingDebt, decimal ReservedCredit, decimal AvailableCredit);
public sealed record DebtEntryResponse(Guid Id, string EntryNumber, string SourceType, Guid? OrderId, string? OrderNumber,
    Guid? DeliveryId, Guid? DeliveryAttemptId, Guid? SourceStockMovementId, decimal FulfillmentValue,
    decimal PrepaymentAppliedAmount, decimal OriginalAmount, decimal TotalPaid, decimal OutstandingAmount,
    DateOnly DueDate, bool IsOverdue, int OverdueDays, string Status, DebtCustomerResponse Customer,
    OrderResponse? Order, IReadOnlyList<DebtActionResponse> Actions, IReadOnlyList<DebtTransactionResponse> Transactions,
    IReadOnlyList<PaymentResponse> Payments, DateTimeOffset CreatedAt);
public sealed record AllocationPreviewItem(Guid DebtEntryId, string EntryNumber, DateOnly DueDate,
    decimal OutstandingAmount, decimal AllocatedAmount);
public sealed record AllocationPreviewResponse(decimal Amount, IReadOnlyList<AllocationPreviewItem> Allocations, decimal UnallocatedAmount);
public sealed record DebtDashboardResponse(decimal TotalOutstandingDebt, decimal TotalOverdueDebt, decimal CollectedToday,
    decimal CollectedThisMonth, int CustomersWithDebt, int CustomersWithOverdueDebt,
    IReadOnlyList<DebtAccountListItem> TopDebtors, IReadOnlyList<PaymentListItem> RecentDebtPayments,
    IReadOnlyList<DebtEntryListItem> OverdueDebts);

public interface IDebtService
{
    Task<PagedResult<DebtEntryListItem>> EntriesAsync(DebtEntryListRequest request, CancellationToken token);
    Task<DebtEntryResponse> EntryAsync(Guid id, CancellationToken token);
    Task<PagedResult<DebtAccountListItem>> AccountsAsync(DebtAccountListRequest request, CancellationToken token);
    Task<DebtAccountResponse> AccountAsync(Guid farmerId, CancellationToken token);
    Task<PagedResult<DebtTransactionResponse>> TransactionsAsync(Guid farmerId, DebtTransactionListRequest request, CancellationToken token);
    Task<AllocationPreviewResponse> PreviewAsync(Guid farmerId, decimal amount, CancellationToken token);
    Task<DebtEntryResponse> ActionAsync(Guid id, string action, string reason, decimal? amount, DateOnly? dueDate, CancellationToken token);
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
    }
}
public sealed class ManualDebtEntryRequestValidator : AbstractValidator<ManualDebtEntryRequest>
{
    public ManualDebtEntryRequestValidator()
    {
        RuleFor(r => r.Amount).Must(a => a > 0 && Credit.CreditMoney.Valid(a));
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.DueDate).NotEmpty();
    }
}
public sealed class DebtDueDateRequestValidator : AbstractValidator<DebtDueDateRequest>
{
    public DebtDueDateRequestValidator()
    { RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000); RuleFor(r => r.NewDueDate).NotEmpty(); }
}
