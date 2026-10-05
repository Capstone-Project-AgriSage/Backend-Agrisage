using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Payments;

namespace AgriSage.Application.Features.Customers;

// Registered customers are FarmerProfiles (§3). WALK_IN identities belong to order snapshots (§34).
public record CustomerRequest
{
    public string FullName { get; init; } = "";
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public CustomerAddressRequest? Address { get; init; }
    public string? Notes { get; init; }
    public Guid? CustomerGroupId { get; init; }
    public bool? AllowCreditPurchase { get; init; }
    public Guid? CreditTierId { get; init; }
    public decimal? CreditLimit { get; init; }
    public string? CreditChangeReason { get; init; }
}

public sealed record CreateCustomerRequest : CustomerRequest
{
    public string Password { get; init; } = "";
    public string CustomerType { get; init; } = "REGISTERED";
}

public sealed record UpdateCustomerRequest : CustomerRequest;
public sealed record CustomerAddressRequest(string RecipientName, string RecipientPhone, string AddressLine,
    string Province, string? Ward = null, string? District = null);
public sealed record CustomerStatusRequest(string Status);
public sealed record AssignCustomerGroupRequest(Guid CustomerGroupId, string? Reason = null);
public sealed record CustomerReference(Guid Id, string Code, string Name);

public sealed record CustomerListRequest : PaginationRequest
{
    public string? Search { get; init; }
    public string? CustomerType { get; init; }
    public Guid? CustomerGroupId { get; init; }
    public string? Status { get; init; }
    public bool? HasDebt { get; init; }
    public string SortBy { get; init; } = "NAME";
    public bool Descending { get; init; }
}

public sealed record CustomerResponse(Guid Id, Guid UserId, string? CustomerCode, string FullName,
    string? PhoneNumber, string? Email, string CustomerType, CustomerReference? CustomerGroup,
    string Status, string? Notes, long TotalOrders, decimal TotalPurchaseAmount, decimal CurrentDebt,
    decimal CreditLimit, bool AllowCreditPurchase, decimal ReservedCredit, decimal AvailableCredit,
    int? PaymentTermDays, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    CustomerAddressRequest? Address = null, CustomerDebtSummaryResponse? DebtSummary = null);

// There is no pending-confirmation AR state in §49; reserved credit is reported separately.
public sealed record CustomerDebtSummaryResponse(decimal TotalOutstandingDebt, decimal ConfirmedDebt,
    decimal? PendingConfirmationDebt, decimal OverdueDebt, decimal TotalPaid, decimal CreditLimit,
    decimal ReservedCredit, decimal AvailableCredit, int OpenDebtCount = 0, DateOnly? OldestDueDate = null,
    bool HasOverdueDebt = false);

public sealed record CustomerOrderListRequest : PaginationRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? PaymentStatus { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}

public sealed record CustomerOrderResponse(Guid OrderId, string OrderNumber, DateTimeOffset OrderDate,
    decimal TotalAmount, IReadOnlyList<string> PaymentMethods, string PaymentStatus, string OrderStatus);

public sealed record CustomerPaymentResponse(PaymentListItem Payment, Guid? ConfirmedBy,
    IReadOnlyList<PaymentAllocationResponse> DebtEntries);

public sealed record GroupAssignmentResponse(Guid Id, CustomerReference CustomerGroup,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid AssignedBy, string? Reason);

public interface ICustomerService
{
    Task<PagedResult<CustomerResponse>> ListAsync(CustomerListRequest request, CancellationToken cancellationToken);
    Task<CustomerResponse> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken);
    Task<CustomerResponse> SetStatusAsync(Guid id, CustomerStatusRequest request, CancellationToken cancellationToken);
    Task<CustomerResponse> AssignGroupAsync(Guid id, AssignCustomerGroupRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupAssignmentResponse>> GroupHistoryAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerDebtSummaryResponse> DebtSummaryAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<CustomerOrderResponse>> OrdersAsync(Guid id, CustomerOrderListRequest request, CancellationToken cancellationToken);
    Task<PagedResult<CustomerPaymentResponse>> PaymentsAsync(Guid id, PaymentListRequest request, CancellationToken cancellationToken);
}
