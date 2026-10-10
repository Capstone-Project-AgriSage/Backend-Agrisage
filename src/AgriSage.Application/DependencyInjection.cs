using AgriSage.Application.Common;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Audit;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Carts;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Deliveries;
using AgriSage.Application.Features.Diagnosis;
using AgriSage.Application.Features.Files;
using AgriSage.Application.Features.GoodsReceipts;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Stocktakes;
using AgriSage.Application.Features.Returns;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Reports;
using AgriSage.Application.Features.Suppliers;
using AgriSage.Application.Features.Products.Interfaces;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Application.Features.Staff.Interfaces;
using AgriSage.Application.Features.Staff.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<AgriSage.Application.Features.Permissions.PermissionEvaluator>();
        services.AddScoped<AgriSage.Application.Features.Permissions.IPermissionEvaluator>(sp => sp.GetRequiredService<AgriSage.Application.Features.Permissions.PermissionEvaluator>());
        services.AddScoped<AgriSage.Application.Features.Permissions.IPermissionConfigurationService, AgriSage.Application.Features.Permissions.PermissionConfigurationService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<AuthSessionService>();
        services.AddScoped<IAuthSessionService>(sp => sp.GetRequiredService<AuthSessionService>());
        services.AddScoped<IAuthChallengeService, AuthChallengeService>();
        services.AddScoped<AuthMaintenanceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<NotificationWriter>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<OperationalAlertsService>();
        services.AddScoped<CommittedNotificationService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<PaymentReconciliationService>();
        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
        services.AddScoped<IUserAccessValidator, UserAccessValidator>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<CustomerWrites>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerGroupService, CustomerGroupService>();
        services.AddScoped<ICustomerCreditService, CustomerCreditService>();
        services.AddOptions<CreditPolicy>();
        services.AddScoped<ICreditEligibilityService, CreditEligibilityService>();
        services.AddScoped<IDebtService, DebtService>();
        services.AddScoped<IBankDebtPaymentService, BankDebtPaymentService>();
        services.AddScoped<CustomerAddresses>();
        services.AddScoped<IMyProfileService, MyProfileService>();
        services.AddScoped<CurrentFarmer>();
        services.AddScoped<ICartService, CartService>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IActiveIngredientService, ActiveIngredientService>();
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IStoreProductService, StoreProductService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IProductImageService, ProductImageService>();
        services.AddScoped<IDeliveryProofService, DeliveryProofService>();
        services.AddScoped<IProofPhotoUsage, DeliveryProofUsage>();

        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<GoodsReceiptConfirmer>();
        services.AddScoped<GoodsReceiptService>();
        services.AddScoped<IGoodsReceiptService>(provider => provider.GetRequiredService<GoodsReceiptService>());
        services.AddScoped<IGoodsReceiptImportService, GoodsReceiptImportService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<StockAdjustmentPosting>();
        services.AddScoped<IStockAdjustmentService, StockAdjustmentService>();
        services.AddScoped<StocktakeQueries>();
        services.AddScoped<IStocktakeService, StocktakeService>();

        services.AddScoped<AuditTrail>();
        services.AddScoped<IPriceListService, PriceListService>();

        services.AddScoped<OrderBuilder>();
        services.AddScoped<OrderQueries>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<OrderConfirmer>();
        services.AddScoped<IOrderConfirmationService, OrderConfirmationService>();
        services.AddScoped<FulfillmentPostingService>();
        services.AddScoped<IOrderPickupService, OrderPickupService>();
        services.AddScoped<OrderCanceller>();
        services.AddScoped<IOrderCancellationService, OrderCancellationService>();
        services.AddScoped<IMeOrderService, MeOrderService>();
        services.AddScoped<PayOsPaymentService>();
        services.AddScoped<IPayOsPaymentService>(sp => sp.GetRequiredService<PayOsPaymentService>());
        services.AddScoped<ICounterSaleService, CounterSaleService>();

        services.AddScoped<ISalesReportService, SalesReportService>();
        services.AddScoped<ReportScope>();
        services.AddScoped<IRevenueReportService, RevenueReportService>();
        services.AddScoped<IOperationalReportService, OperationalReportService>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();
        services.AddScoped<IDebtReportService, DebtReportService>();

        services.AddScoped<DeliveryAccess>();
        services.AddScoped<DeliveryQueries>();
        services.AddScoped<DeliveryProofUrls>();
        services.AddScoped<IDeliveryService, DeliveryService>();
        services.AddScoped<IDeliveryAttemptService, DeliveryAttemptService>();
        services.AddScoped<IDeliveryIncidentService, DeliveryIncidentService>();
        services.AddScoped<IMyDeliveryService, MyDeliveryService>();
        services.AddScoped<IDeliveryReportService, DeliveryReportService>();

        services.AddScoped<PaymentAllocator>();
        services.AddScoped<PaymentQueries>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IMyPaymentService, MyPaymentService>();

        // Real shared steps (docs/reference/api-flows/README.md §4); the caller owns SaveChanges and commit.
        services.AddScoped<IPriceResolver, PriceResolver>();                                      // F1.1 (real)
        services.AddScoped<IOrderSettlementGuard, OrderSettlementGuard>();
        services.AddScoped<ICreditReservationAdjuster, CreditReservationAdjuster>();
        services.AddScoped<IFulfillmentFinancialPosting, FulfillmentFinancialPosting>();
        services.AddScoped<IDebtRepaymentPosting, DebtRepaymentPosting>();
        services.AddScoped<IDebtReturnPosting, DebtReturnPosting>();
        services.AddScoped<IOrderPrepaymentLedger, OrderPrepaymentLedger>();                      // F1.3 (real)
        services.AddScoped<IOrderPaymentCancellation, OrderPaymentCancellation>();                // F1.6 (real)

        services.AddScoped<ReturnSources>();
        services.AddScoped<SalesReturnQueries>();
        services.AddScoped<SalesReturnService>();
        services.AddScoped<ISalesReturnService>(sp => sp.GetRequiredService<SalesReturnService>());
        services.AddScoped<IMySalesReturnService, MySalesReturnService>();
        services.AddScoped<IRefundService, RefundService>();

        services.AddScoped<AiReviewer>();
        services.AddScoped<DiagnosisImageUrls>();
        services.AddScoped<DiagnosisAiRunner>();
        services.AddScoped<IMyDiagnosisCaseService, MyDiagnosisCaseService>();
        services.AddScoped<IDiagnosisCaseService, DiagnosisCaseService>();
        services.AddScoped<IAiModelService, AiModelService>();
        services.AddScoped<IDiseaseService, DiseaseService>();

        // Further feature application services are registered here by later tasks.

        return services;
    }
}
