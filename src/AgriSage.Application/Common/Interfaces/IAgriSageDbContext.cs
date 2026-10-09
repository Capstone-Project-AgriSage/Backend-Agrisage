using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Content.Entities;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.GoodsReceipts.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Suppliers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgriSage.Application.Common.Interfaces;

// Application persistence abstraction (architecture §13). Implemented by Infrastructure's AgriSageDbContext.
// Child entities have sets for querying only: they are created and changed through their aggregate roots.
public interface IAgriSageDbContext
{
    // Identity
    DbSet<Role> Roles { get; }

    DbSet<User> Users { get; }

    DbSet<AuthSession> AuthSessions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AuthChallenge> AuthChallenges { get; }

    DbSet<UserAddress> UserAddresses { get; }

    // Stores
    DbSet<Store> Stores { get; }

    DbSet<StoreMember> StoreMembers { get; }

    // Customers
    DbSet<FarmerProfile> FarmerProfiles { get; }

    DbSet<CustomerGroup> CustomerGroups { get; }

    DbSet<CustomerGroupAssignment> CustomerGroupAssignments { get; }

    // Products
    DbSet<Category> Categories { get; }

    DbSet<Brand> Brands { get; }

    DbSet<Product> Products { get; }

    DbSet<Unit> Units { get; }

    DbSet<ProductPackaging> ProductPackagings { get; }

    DbSet<ActiveIngredient> ActiveIngredients { get; }

    DbSet<ProductActiveIngredient> ProductActiveIngredients { get; }

    DbSet<StoreProduct> StoreProducts { get; }

    DbSet<ProductReview> ProductReviews { get; }

    // Pricing
    DbSet<PriceList> PriceLists { get; }

    DbSet<PriceListItem> PriceListItems { get; }

    DbSet<CustomerGroupPriceList> CustomerGroupPriceLists { get; }

    // Suppliers / Goods Receipts
    DbSet<Supplier> Suppliers { get; }

    DbSet<GoodsReceipt> GoodsReceipts { get; }

    DbSet<GoodsReceiptItem> GoodsReceiptItems { get; }

    // Inventory
    DbSet<InventoryLot> InventoryLots { get; }

    DbSet<InventoryLotBalance> InventoryLotBalances { get; }

    DbSet<StockMovement> StockMovements { get; }

    DbSet<StockMovementItem> StockMovementItems { get; }

    DbSet<Stocktake> Stocktakes { get; }

    DbSet<StocktakeItem> StocktakeItems { get; }

    DbSet<InventoryReservation> InventoryReservations { get; }

    DbSet<InventoryReservationItem> InventoryReservationItems { get; }

    // Orders
    DbSet<Cart> Carts { get; }

    DbSet<CartItem> CartItems { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderItem> OrderItems { get; }

    // Payments
    DbSet<Payment> Payments { get; }

    DbSet<PaymentAllocation> PaymentAllocations { get; }

    // Deliveries
    DbSet<Delivery> Deliveries { get; }

    DbSet<DeliveryItem> DeliveryItems { get; }

    DbSet<DeliveryItemLotAllocation> DeliveryItemLotAllocations { get; }

    DbSet<DeliveryAttempt> DeliveryAttempts { get; }

    DbSet<DeliveryAttemptItem> DeliveryAttemptItems { get; }

    DbSet<DeliveryIncident> DeliveryIncidents { get; }

    // Credit
    DbSet<CreditTier> CreditTiers { get; }

    DbSet<FarmerCreditProfile> FarmerCreditProfiles { get; }

    DbSet<CreditLimitHistory> CreditLimitHistories { get; }

    DbSet<CreditReservation> CreditReservations { get; }

    // Debt
    DbSet<DebtAccount> DebtAccounts { get; }

    DbSet<DebtEntry> DebtEntries { get; }

    DbSet<DebtEntryAction> DebtEntryActions { get; }

    DbSet<DebtTransaction> DebtTransactions { get; }

    // Returns
    DbSet<SalesReturn> SalesReturns { get; }

    DbSet<SalesReturnItem> SalesReturnItems { get; }

    DbSet<Refund> Refunds { get; }

    // Diagnosis
    DbSet<Disease> Diseases { get; }

    DbSet<DiseaseTreatment> DiseaseTreatments { get; }

    DbSet<AiModel> AiModels { get; }

    DbSet<AiPolicyConfig> AiPolicyConfigs { get; }

    DbSet<DiagnosisCase> DiagnosisCases { get; }

    DbSet<DiagnosisImage> DiagnosisImages { get; }

    DbSet<AiInference> AiInferences { get; }

    DbSet<AgentReview> AgentReviews { get; }

    DbSet<RecommendationItem> RecommendationItems { get; }

    // Content / Notifications / Audit
    DbSet<Article> Articles { get; }

    DbSet<ContactRequest> ContactRequests { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<NotificationOutbox> NotificationOutbox { get; }

    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // For the single transaction owner of a cross-aggregate use case (architecture §19).
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
