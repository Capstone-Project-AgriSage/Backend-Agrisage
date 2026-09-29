using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Credit.Entities;

// Default credit policy; the individual Farmer Credit Profile remains authoritative.
public sealed class CreditTier : SoftDeletableEntity
{
    private CreditTier()
    {
    }

    public CreditTier(
        Guid storeId,
        string code,
        string name,
        decimal defaultCreditLimit,
        int defaultPaymentTermDays,
        string? description = null)
    {
        StoreId = storeId;
        Code = Guard.NotNullOrWhiteSpace(code);
        Update(name, description, defaultCreditLimit, defaultPaymentTermDays);
        IsActive = true;
    }

    public Guid StoreId { get; private set; }

    // Tier code values are examples only in the database design (STANDARD, SILVER, ...).
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public decimal DefaultCreditLimit { get; private set; }

    public int DefaultPaymentTermDays { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? description, decimal defaultCreditLimit, int defaultPaymentTermDays)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
        DefaultCreditLimit = Guard.NonNegativeMoney(defaultCreditLimit);
        DefaultPaymentTermDays = (int)Guard.NotNegative(defaultPaymentTermDays);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
