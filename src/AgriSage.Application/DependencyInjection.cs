using AgriSage.Application.Common;
using AgriSage.Application.Common.Placeholders;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Files;
using AgriSage.Application.Features.GoodsReceipts;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Pricing;
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

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
        services.AddScoped<IUserAccessValidator, UserAccessValidator>();
        services.AddScoped<IStaffService, StaffService>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IActiveIngredientService, ActiveIngredientService>();
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IStoreProductService, StoreProductService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IProductImageService, ProductImageService>();
        services.AddScoped<IDeliveryProofService, DeliveryProofService>();

        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<GoodsReceiptConfirmer>();
        services.AddScoped<GoodsReceiptService>();
        services.AddScoped<IGoodsReceiptService>(provider => provider.GetRequiredService<GoodsReceiptService>());
        services.AddScoped<IGoodsReceiptImportService, GoodsReceiptImportService>();
        services.AddScoped<IInventoryService, InventoryService>();

        services.AddScoped<AuditTrail>();
        services.AddScoped<IPriceListService, PriceListService>();

        services.AddScoped<OrderBuilder>();
        services.AddScoped<OrderQueries>();
        services.AddScoped<IOrderService, OrderService>();

        // Cross-flow interfaces (docs/reference/api-flows/README.md §4). Each line is replaced by its owner task with
        // the real implementation; the Temporary* class is then deleted (Common/Placeholders).
        services.AddScoped<IPriceResolver, PriceResolver>();                                      // F1.1 (real)
        services.AddScoped<IOrderSettlementGuard, TemporaryOrderSettlementGuard>();               // F3.3
        services.AddScoped<ICreditReservationAdjuster, TemporaryCreditReservationAdjuster>();     // F3.3
        services.AddScoped<IFulfillmentFinancialPosting, TemporaryFulfillmentFinancialPosting>(); // F3.4
        services.AddScoped<IDebtRepaymentPosting, TemporaryDebtRepaymentPosting>();               // F3.5
        services.AddScoped<IDebtReturnPosting, TemporaryDebtReturnPosting>();                     // F3.5
        services.AddScoped<IOrderPrepaymentLedger, TemporaryOrderPrepaymentLedger>();             // F1.3
        services.AddScoped<IOrderPaymentCancellation, TemporaryOrderPaymentCancellation>();       // F1.6

        // Further feature application services are registered here by later tasks.

        return services;
    }
}
