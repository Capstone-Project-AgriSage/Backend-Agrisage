using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Orders;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

public sealed record OrderDraft(
    OrderSource Source,
    CustomerType CustomerType,
    SettlementType SettlementType,
    FulfillmentType FulfillmentType,
    IReadOnlyList<OrderItemRequest> Lines,
    Guid? FarmerProfileId = null,
    string? CustomerName = null,
    string? CustomerPhone = null,
    Guid? AddressId = null,
    DeliveryAddressRequest? DeliveryAddress = null,
    string? Note = null,
    // Staff only: lets a line carry a price different from the suggested one (decision D4). Online checkout passes false.
    bool AllowPriceOverride = false);

// A line checked against the catalog and the order's price list, ready for Order.AddItem.
public sealed record ResolvedOrderLine(
    StoreProduct StoreProduct,
    ProductPackaging Packaging,
    string Sku,
    string ProductName,
    string PackagingName,
    long Quantity,
    decimal SuggestedUnitPrice,
    PriceOverride? PriceOverride);

// Shared step (api-flows README §3.2 / §4.9; task F1.2): builds a PENDING_CONFIRMATION order with its number and the
// customer, price list, address and line snapshots. It reads only; it never saves and does not add the order to the
// context, so the caller (counter order, online checkout, quick sale) decides what is saved together.
public sealed class OrderBuilder(
    IAgriSageDbContext context,
    IPriceResolver prices,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    AuditTrail audit)
{
    public const string WalkInDefaultName = "Khách lẻ";

    public async Task<Order> BuildAsync(OrderDraft draft, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var now = clock.UtcNow;

        var (farmer, name, phone) = await ResolveCustomerAsync(draft, cancellationToken);
        var priceContext = await prices.GetContextAsync(farmer?.Id, now, cancellationToken);
        var lines = await ResolveLinesAsync(
            storeId, priceContext.PriceListId, draft.Lines, draft.AllowPriceOverride, cancellationToken);

        DeliveryAddress? address = null;
        Guid? sourceAddressId = null;
        if (draft.FulfillmentType == FulfillmentType.Delivery)
        {
            (address, sourceAddressId) = await ResolveDeliveryAddressAsync(
                farmer?.UserId, draft.AddressId, draft.DeliveryAddress, cancellationToken);
        }
        else if (draft.AddressId is not null || draft.DeliveryAddress is not null)
        {
            throw new BusinessRuleException("A PICKUP order has no delivery address.");
        }

        var number = await DocumentNumbers.NextAsync(
            context.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.StoreId == storeId).Select(o => o.OrderNumber),
            DocumentNumbers.Order,
            BusinessCalendar.Today(now),
            cancellationToken);

        var order = new Order(
            storeId, number, draft.Source, draft.CustomerType, actor, name, draft.SettlementType, draft.FulfillmentType,
            farmer?.Id, phone, priceContext.CustomerGroupId, priceContext.PriceListId, address, sourceAddressId,
            Texts.Clean(draft.Note));

        foreach (var line in lines)
        {
            AddLine(order, line);
        }

        return order;
    }

    // Adds the resolved line to the order and returns the new item.
    public static OrderItem AddLine(Order order, ResolvedOrderLine line) =>
        order.AddItem(
            line.StoreProduct, line.Packaging, line.Sku, line.ProductName, line.PackagingName, line.Quantity,
            line.SuggestedUnitPrice, line.PriceOverride);

    // Sellable ACTIVE store products and ACTIVE sale packagings, priced by the given list. Every problem is reported
    // per line (items[i]) in one 422. Used for new orders and for lines added to an existing order, which keep the
    // order's own price list instead of resolving it again.
    public async Task<IReadOnlyList<ResolvedOrderLine>> ResolveLinesAsync(
        Guid storeId,
        Guid priceListId,
        IReadOnlyList<OrderItemRequest> lines,
        bool allowPriceOverride,
        CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var storeProductIds = lines.Select(l => l.StoreProductId).Distinct().ToList();
        var packagingIds = lines.Select(l => l.ProductPackagingId).Distinct().ToList();

        var storeProducts = await context.StoreProducts.AsNoTracking().Include(sp => sp.Product)
            .Where(sp => sp.StoreId == storeId && storeProductIds.Contains(sp.Id))
            .ToDictionaryAsync(sp => sp.Id, cancellationToken);
        var packagings = await context.ProductPackagings.AsNoTracking().Include(p => p.Unit)
            .Where(p => packagingIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var suggested = await prices.GetPricesAsync(
            priceListId,
            lines.Select(l => new PriceLine(l.StoreProductId, l.ProductPackagingId)).Distinct().ToList(),
            cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var resolved = new List<ResolvedOrderLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var problem = Check(line, storeProducts, packagings, suggested, out var storeProduct, out var packaging, out var price);

            PriceOverride? priceOverride = null;
            if (problem is null && allowPriceOverride && line.UnitPrice is { } unitPrice && unitPrice != price)
            {
                var reason = Texts.Clean(line.OverrideReason);
                if (reason is null)
                {
                    problem = "A price different from the suggested price needs a reason.";
                }
                else
                {
                    priceOverride = new PriceOverride(unitPrice, actor, reason);
                }
            }

            if (problem is not null)
            {
                errors[$"items[{index}]"] = [problem];
                continue;
            }

            resolved.Add(new ResolvedOrderLine(
                storeProduct!, packaging!, storeProduct!.Product.Sku, storeProduct.Product.Name,
                packaging!.PackagingName ?? packaging.Unit.Name, line.Quantity, price, priceOverride));
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException($"{errors.Count} line(s) cannot be ordered; nothing was saved.", errors);
        }

        return resolved;
    }

    // The saved-address snapshot (addressId: one of the customer's own addresses) or the typed one; exactly one.
    public async Task<(DeliveryAddress Address, Guid? SourceAddressId)> ResolveDeliveryAddressAsync(
        Guid? customerUserId, Guid? addressId, DeliveryAddressRequest? typed, CancellationToken cancellationToken)
    {
        if ((addressId is null) == (typed is null))
        {
            throw new BusinessRuleException("A DELIVERY order needs exactly one of addressId or deliveryAddress.");
        }

        if (addressId is { } id)
        {
            var saved = customerUserId is { } userId
                ? await context.UserAddresses.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken)
                : null;

            return saved is null
                ? throw new BusinessRuleException("addressId must be one of the customer's saved addresses.")
                : (new DeliveryAddress(
                    saved.RecipientName, saved.RecipientPhone, saved.AddressLine, saved.Province, saved.Ward, saved.District,
                    saved.Latitude, saved.Longitude), id);
        }

        if (!ContactNormalizer.TryNormalizePhone(typed!.RecipientPhone, out var phone))
        {
            throw new BusinessRuleException("The recipient phone must be a valid Vietnamese mobile number.");
        }

        return (new DeliveryAddress(
            typed.RecipientName.Trim(), phone!, typed.AddressLine.Trim(), typed.Province.Trim(), Texts.Clean(typed.Ward),
            Texts.Clean(typed.District), typed.Latitude, typed.Longitude), null);
    }

    // One PRICE_OVERRIDE audit row for an item that carries an override, in the caller's unit of work.
    public void RecordPriceOverride(Order order, OrderItem item) =>
        audit.Record(
            "PRICE_OVERRIDE",
            "ORDER",
            order.Id,
            order.StoreId,
            new { item.SuggestedUnitPrice },
            new { orderItemId = item.Id, sku = item.ProductSkuSnapshot, item.UnitPrice },
            item.OverrideReason);

    public void RecordPriceOverrides(Order order)
    {
        foreach (var item in order.Items.Where(i => i.PriceOverridden))
        {
            RecordPriceOverride(order, item);
        }
    }

    private async Task<(FarmerProfile? Farmer, string Name, string? Phone)> ResolveCustomerAsync(
        OrderDraft draft, CancellationToken cancellationToken)
    {
        if (draft.CustomerType == CustomerType.Registered)
        {
            var id = draft.FarmerProfileId ?? throw new BusinessRuleException("A REGISTERED order needs a farmerProfileId.");
            var farmer = await context.FarmerProfiles.AsNoTracking().Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException("Farmer profile", id);

            // Name and phone always come from the Farmer; client values are ignored.
            return (farmer, farmer.User.FullName, farmer.User.PhoneNumber);
        }

        if (draft.FarmerProfileId is not null)
        {
            throw new BusinessRuleException("A WALK_IN order cannot reference a farmer.");
        }

        if (draft.SettlementType != SettlementType.FullPayment)
        {
            throw new BusinessRuleException("A walk-in customer cannot buy on credit.");
        }

        string? phone = null;
        if (Texts.Clean(draft.CustomerPhone) is { } typedPhone)
        {
            phone = ContactNormalizer.TryNormalizePhone(typedPhone, out var normalized)
                ? normalized
                : throw new BusinessRuleException("The customer phone must be a valid Vietnamese mobile number.");
        }

        return (null, Texts.Clean(draft.CustomerName) ?? WalkInDefaultName, phone);
    }

    // Whether a store product and packaging can be ordered now. Used when the order is built and again when it is
    // confirmed (a product can be switched off in between).
    public static string? GetSellabilityProblem(StoreProduct storeProduct, ProductPackaging? packaging)
    {
        if (!storeProduct.IsSellable || !storeProduct.IsActive || storeProduct.Product.Status != ProductStatus.Active)
        {
            return "The product is not for sale.";
        }

        if (packaging is null || packaging.ProductId != storeProduct.ProductId)
        {
            return "The packaging does not belong to this product.";
        }

        return packaging.Status != PackagingStatus.Active || !packaging.IsSaleUnit
            ? "Only ACTIVE sale packagings can be ordered."
            : null;
    }

    private static string? Check(
        OrderItemRequest line,
        Dictionary<Guid, StoreProduct> storeProducts,
        Dictionary<Guid, ProductPackaging> packagings,
        IReadOnlyDictionary<PriceLine, decimal> suggested,
        out StoreProduct? storeProduct,
        out ProductPackaging? packaging,
        out decimal price)
    {
        packaging = null;
        price = 0m;

        if (!storeProducts.TryGetValue(line.StoreProductId, out storeProduct))
        {
            return "The store product does not exist in this store.";
        }

        packagings.TryGetValue(line.ProductPackagingId, out packaging);
        if (GetSellabilityProblem(storeProduct, packaging) is { } problem)
        {
            return problem;
        }

        return suggested.TryGetValue(new PriceLine(line.StoreProductId, line.ProductPackagingId), out price)
            ? null
            : "The price list has no price for this packaging.";
    }
}
