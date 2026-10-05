using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Debt;

// Table 51: debt_transactions. Immutable AR ledger created only through DebtAccount.
internal sealed class DebtTransactionConfiguration : EntityConfiguration<DebtTransaction>
{
    protected override string TableName => "debt_transactions";

    protected override void ConfigureEntity(EntityTypeBuilder<DebtTransaction> builder)
    {
        builder.Property(transaction => transaction.TransactionType).HasMaxLength(30);
        builder.Property(transaction => transaction.AmountDelta).IsMoney();
        builder.Property(transaction => transaction.BalanceAfter).IsMoney();
        builder.Property(transaction => transaction.Status).HasMaxLength(20);
        builder.Property(transaction => transaction.Note).HasMaxLength(1000);

        builder.HasReference<DebtTransaction, DebtAccount>(transaction => transaction.DebtAccountId);
        builder.HasReference<DebtTransaction, DebtEntry>(transaction => transaction.DebtEntryId);
        builder.HasReference<DebtTransaction, PaymentAllocation>(transaction => transaction.PaymentAllocationId);
        builder.HasReference<DebtTransaction, SalesReturn>(transaction => transaction.SalesReturnId);
        builder.HasReference<DebtTransaction, DebtEntryAction>(transaction => transaction.DebtEntryActionId);
        builder.HasReference<DebtTransaction, DebtTransaction>(transaction => transaction.ReversalOfTransactionId);
        builder.HasUserReference(transaction => transaction.CreatedBy);

        builder.HasIndex(transaction => new { transaction.DebtAccountId, transaction.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(transaction => new { transaction.DebtEntryId, transaction.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(transaction => transaction.PaymentAllocationId, "ux_debt_transactions_payment_source")
            .HasDatabaseName("ux_debt_transactions_payment_source").IsUnique().HasFilter("payment_allocation_id IS NOT NULL");

        builder.HasCheck("amount_delta", "amount_delta <> 0");
        builder.HasCheck("balance_after", "balance_after >= 0");
        builder.HasCheck(
            "payment_allocation_source",
            "transaction_type <> 'PAYMENT' OR payment_allocation_id IS NOT NULL");
    }
}
