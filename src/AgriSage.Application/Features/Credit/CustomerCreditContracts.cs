using AgriSage.Application.Features.Customers;
using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Credit;

public sealed record CreateCustomerCreditRequest(Guid? CreditTierId = null, decimal? CreditLimit = null, string? Note = null);
public sealed record CustomerCreditLimitRequest(decimal CreditLimit, string Reason, Guid? CreditTierId = null);
public sealed record CustomerCreditStatusRequest(string Reason);
public sealed record CreditSummaryResponse(Guid ProfileId, Guid FarmerProfileId, string Status, CustomerReference? CreditTier,
    decimal CreditLimit, int? PaymentTermDays, decimal OutstandingReceivable, decimal ReservedCredit, decimal AvailableCredit,
    Guid ApprovedBy, DateTimeOffset ApprovedAt, string? Note, long Version, bool AllowCreditPurchase = false,
    decimal OverdueAmount = 0, bool HasOverdueDebt = false, decimal TotalPaid = 0, int OpenDebtCount = 0, DateOnly? OldestDueDate = null);
public sealed record CreditLimitHistoryResponse(Guid Id, CustomerReference? OldCreditTier, CustomerReference? NewCreditTier,
    decimal OldCreditLimit, decimal NewCreditLimit, string Reason, Guid ChangedBy, DateTimeOffset ChangedAt);
public sealed record CreditReservationResponse(Guid Id, Guid OrderId, string OrderNumber, decimal ReservedAmount,
    decimal ConsumedAmount, decimal ReleasedAmount, decimal RemainingAmount, string Status, DateTimeOffset CreatedAt);
public sealed record CreditTierRequest(string Code, string Name, decimal DefaultCreditLimit, int DefaultPaymentTermDays, string? Description = null);
public sealed record UpdateCreditTierRequest(string Name, decimal DefaultCreditLimit, int DefaultPaymentTermDays, string? Description = null);
public sealed record CreditTierResponse(Guid Id, string Code, string Name, string? Description, decimal DefaultCreditLimit,
    int DefaultPaymentTermDays, bool IsActive, long ProfileCount, IReadOnlyList<CustomerReference> Groups);

public interface ICustomerCreditService
{
    Task<MyCreditSummaryResponse> GetOwnAsync(CancellationToken cancellationToken);
    Task<CreditSummaryResponse> GetAsync(Guid farmerId, CancellationToken cancellationToken);
    Task<CreditSummaryResponse> CreateAsync(Guid farmerId, CreateCustomerCreditRequest request, CancellationToken cancellationToken);
    Task<CreditSummaryResponse> LimitAsync(Guid farmerId, CustomerCreditLimitRequest request, CancellationToken cancellationToken);
    Task<CreditSummaryResponse> StatusAsync(Guid farmerId, string status, CustomerCreditStatusRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CreditLimitHistoryResponse>> HistoryAsync(Guid farmerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CreditReservationResponse>> ReservationsAsync(Guid farmerId, bool activeOnly, CancellationToken cancellationToken);
    Task<PagedResult<CreditTierResponse>> TiersAsync(CustomerGroupListRequest request, CancellationToken cancellationToken);
    Task<CreditTierResponse> TierAsync(Guid id, CancellationToken cancellationToken);
    Task<CreditTierResponse> CreateTierAsync(CreditTierRequest request, CancellationToken cancellationToken);
    Task<CreditTierResponse> UpdateTierAsync(Guid id, UpdateCreditTierRequest request, CancellationToken cancellationToken);
    Task<CreditTierResponse> SetTierActiveAsync(Guid id, bool active, CancellationToken cancellationToken);
}

public sealed record MyCreditSummaryResponse(Guid ProfileId, Guid FarmerProfileId, string Status, CustomerReference? CreditTier,
    decimal CreditLimit, int? PaymentTermDays, decimal OutstandingReceivable, decimal ReservedCredit, decimal AvailableCredit);
