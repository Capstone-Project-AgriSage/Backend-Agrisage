using AgriSage.Application.Common.Placeholders;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Files;
using AgriSage.Application.Features.GoodsReceipts;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Payments;
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
        services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
        services.AddScoped<IInventoryService, InventoryService>();

        // Cross-module interfaces (API_CONTRACT_*.md). Each line is replaced by its owner task with the real
        // implementation; the Temporary* class is then deleted (Common/Placeholders).
        services.AddScoped<IPriceResolver, TemporaryPriceResolver>();                             // B3
        services.AddScoped<IOrderSettlementGuard, TemporaryOrderSettlementGuard>();               // B4
        services.AddScoped<ICreditReservationAdjuster, TemporaryCreditReservationAdjuster>();     // B4
        services.AddScoped<IFulfillmentFinancialPosting, TemporaryFulfillmentFinancialPosting>(); // B5
        services.AddScoped<IDebtRepaymentPosting, TemporaryDebtRepaymentPosting>();               // B5
        services.AddScoped<IDebtReturnPosting, TemporaryDebtReturnPosting>();                     // B5
        services.AddScoped<IOrderPrepaymentLedger, TemporaryOrderPrepaymentLedger>();             // C1
        services.AddScoped<IOrderPaymentCancellation, TemporaryOrderPaymentCancellation>();       // C1

        // Further feature application services are registered here by later tasks.

        return services;
    }
}
