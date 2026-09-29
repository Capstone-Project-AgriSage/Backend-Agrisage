using AgriSage.Application.Common.Interfaces;
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
using AgriSage.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgriSage.Infrastructure.Persistence;

// EF Core model of the 67 AgriSage tables. Mappings live in Configurations/<Feature>/;
// model-wide rules (names, enums, delete behavior, soft-delete filters, concurrency tokens) in Conventions/;
// timestamps, soft delete and versions are set on save by Interceptors/ (registered in DependencyInjection).
public sealed class AgriSageDbContext : DbContext, IAgriSageDbContext
{
    public AgriSageDbContext(DbContextOptions<AgriSageDbContext> options)
        : base(options)
    {
        // Remove() becomes a soft delete inside SaveChanges (SoftDeleteInterceptor). Cascading at Remove() time
        // would first null the FKs of tracked dependents under Restrict/NoAction, so it is deferred to
        // SaveChanges, by which time the entry is no longer Deleted.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
    }

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<StoreMember> StoreMembers => Set<StoreMember>();

    public DbSet<FarmerProfile> FarmerProfiles => Set<FarmerProfile>();

    public DbSet<CustomerGroup> CustomerGroups => Set<CustomerGroup>();

    public DbSet<CustomerGroupAssignment> CustomerGroupAssignments => Set<CustomerGroupAssignment>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<ProductPackaging> ProductPackagings => Set<ProductPackaging>();

    public DbSet<ActiveIngredient> ActiveIngredients => Set<ActiveIngredient>();

    public DbSet<ProductActiveIngredient> ProductActiveIngredients => Set<ProductActiveIngredient>();

    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();

    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

    public DbSet<PriceList> PriceLists => Set<PriceList>();

    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();

    public DbSet<CustomerGroupPriceList> CustomerGroupPriceLists => Set<CustomerGroupPriceList>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();

    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();

    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();

    public DbSet<InventoryLotBalance> InventoryLotBalances => Set<InventoryLotBalance>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<StockMovementItem> StockMovementItems => Set<StockMovementItem>();

    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();

    public DbSet<StocktakeItem> StocktakeItems => Set<StocktakeItem>();

    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    public DbSet<InventoryReservationItem> InventoryReservationItems => Set<InventoryReservationItem>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<DeliveryItem> DeliveryItems => Set<DeliveryItem>();

    public DbSet<DeliveryItemLotAllocation> DeliveryItemLotAllocations => Set<DeliveryItemLotAllocation>();

    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public DbSet<DeliveryAttemptItem> DeliveryAttemptItems => Set<DeliveryAttemptItem>();

    public DbSet<DeliveryIncident> DeliveryIncidents => Set<DeliveryIncident>();

    public DbSet<CreditTier> CreditTiers => Set<CreditTier>();

    public DbSet<FarmerCreditProfile> FarmerCreditProfiles => Set<FarmerCreditProfile>();

    public DbSet<CreditLimitHistory> CreditLimitHistories => Set<CreditLimitHistory>();

    public DbSet<CreditReservation> CreditReservations => Set<CreditReservation>();

    public DbSet<DebtAccount> DebtAccounts => Set<DebtAccount>();

    public DbSet<DebtEntry> DebtEntries => Set<DebtEntry>();

    public DbSet<DebtEntryAction> DebtEntryActions => Set<DebtEntryAction>();

    public DbSet<DebtTransaction> DebtTransactions => Set<DebtTransaction>();

    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();

    public DbSet<SalesReturnItem> SalesReturnItems => Set<SalesReturnItem>();

    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<Disease> Diseases => Set<Disease>();

    public DbSet<DiseaseTreatment> DiseaseTreatments => Set<DiseaseTreatment>();

    public DbSet<AiModel> AiModels => Set<AiModel>();

    public DbSet<AiPolicyConfig> AiPolicyConfigs => Set<AiPolicyConfig>();

    public DbSet<DiagnosisCase> DiagnosisCases => Set<DiagnosisCase>();

    public DbSet<DiagnosisImage> DiagnosisImages => Set<DiagnosisImage>();

    public DbSet<AiInference> AiInferences => Set<AiInference>();

    public DbSet<AgentReview> AgentReviews => Set<AgentReview>();

    public DbSet<RecommendationItem> RecommendationItems => Set<RecommendationItem>();

    public DbSet<Article> Articles => Set<Article>();

    public DbSet<ContactRequest> ContactRequests => Set<ContactRequest>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    // FK indexes are created by PersistenceConventions instead (operational FKs only, not actor FKs).
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgriSageDbContext).Assembly);
        PersistenceConventions.Apply(modelBuilder);
    }
}
