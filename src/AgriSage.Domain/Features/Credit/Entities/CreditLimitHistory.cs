using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Credit.Entities;

// History of one Credit Tier / Credit Limit change. Created only by FarmerCreditProfile.ChangeCreditLimit.
public sealed class CreditLimitHistory : SoftDeletableChildEntity
{
    private CreditLimitHistory()
    {
    }

    internal CreditLimitHistory(
        Guid farmerCreditProfileId,
        Guid? oldCreditTierId,
        Guid? newCreditTierId,
        decimal oldCreditLimit,
        decimal newCreditLimit,
        string reason,
        Guid changedBy,
        DateTimeOffset changedAt)
    {
        FarmerCreditProfileId = farmerCreditProfileId;
        OldCreditTierId = oldCreditTierId;
        NewCreditTierId = newCreditTierId;
        OldCreditLimit = oldCreditLimit;
        NewCreditLimit = newCreditLimit;
        Reason = Guard.NotNullOrWhiteSpace(reason);
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }

    public Guid FarmerCreditProfileId { get; private set; }

    public Guid? OldCreditTierId { get; private set; }

    public Guid? NewCreditTierId { get; private set; }

    public decimal OldCreditLimit { get; private set; }

    public decimal NewCreditLimit { get; private set; }

    public string Reason { get; private set; } = null!;

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }
}
