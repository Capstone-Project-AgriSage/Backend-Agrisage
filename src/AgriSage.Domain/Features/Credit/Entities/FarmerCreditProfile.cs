using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Customers.Entities;

namespace AgriSage.Domain.Features.Credit.Entities;

// Current individual credit policy of a registered Farmer. Outstanding receivable and reserved credit are
// NOT stored here; the calling use case reads them (Debt Account balance, active Credit Reservations)
// inside the same transaction and passes them in (database design §XIX).
public sealed class FarmerCreditProfile : SoftDeletableEntity, IHasConcurrencyVersion
{
    private readonly List<CreditLimitHistory> _limitHistories = [];

    private FarmerCreditProfile()
    {
    }

    public FarmerCreditProfile(
        Guid storeId,
        Guid farmerProfileId,
        decimal creditLimit,
        Guid approvedBy,
        DateTimeOffset approvedAt,
        Guid? creditTierId = null,
        string? note = null)
    {
        StoreId = storeId;
        FarmerProfileId = farmerProfileId;
        CreditLimit = Guard.NonNegativeMoney(creditLimit);
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        CreditTierId = creditTierId;
        Note = note;
        Status = FarmerCreditProfileStatus.Active;
    }

    public Guid StoreId { get; private set; }

    public Guid FarmerProfileId { get; private set; }

    public FarmerProfile FarmerProfile { get; private set; } = null!;

    public Guid? CreditTierId { get; private set; }

    public CreditTier? CreditTier { get; private set; }

    public decimal CreditLimit { get; private set; }

    public FarmerCreditProfileStatus Status { get; private set; }

    public Guid ApprovedBy { get; private set; }

    public DateTimeOffset ApprovedAt { get; private set; }

    public string? Note { get; private set; }

    public long Version { get; private set; }

    public IReadOnlyCollection<CreditLimitHistory> LimitHistories => _limitHistories.AsReadOnly();

    // Credit is available only while the Profile is ACTIVE.
    public bool CanUseCredit => Status == FarmerCreditProfileStatus.Active;

    // Every limit/tier change is historical; the audit log entry is written by the calling use case.
    public CreditLimitHistory ChangeCreditLimit(
        decimal newCreditLimit,
        Guid? newCreditTierId,
        string reason,
        Guid changedBy,
        DateTimeOffset changedAt)
    {
        Guard.NonNegativeMoney(newCreditLimit);

        if (newCreditLimit == CreditLimit && newCreditTierId == CreditTierId)
        {
            throw new DomainException("The credit limit and tier are unchanged.");
        }

        var history = new CreditLimitHistory(
            Id,
            CreditTierId,
            newCreditTierId,
            CreditLimit,
            newCreditLimit,
            reason,
            changedBy,
            changedAt);

        _limitHistories.Add(history);
        CreditLimit = newCreditLimit;
        CreditTierId = newCreditTierId;

        return history;
    }

    public void ChangeStatus(FarmerCreditProfileStatus status) => Status = status;

    // Available credit = credit limit − outstanding receivable − remaining reserved credit.
    public decimal CalculateAvailableCredit(decimal outstandingReceivable, decimal remainingReservedCredit) =>
        CreditLimit - Guard.NotNegative(outstandingReceivable) - Guard.NotNegative(remainingReservedCredit);

    public void EnsureCanReserve(decimal requiredCredit, decimal outstandingReceivable, decimal remainingReservedCredit)
    {
        if (!CanUseCredit)
        {
            throw new DomainException($"Credit profile is {Status}; credit is not available.");
        }

        var available = CalculateAvailableCredit(outstandingReceivable, remainingReservedCredit);

        if (Guard.NonNegativeMoney(requiredCredit) > available)
        {
            throw new DomainException($"Required credit {requiredCredit} exceeds available credit {available}.");
        }
    }
}
