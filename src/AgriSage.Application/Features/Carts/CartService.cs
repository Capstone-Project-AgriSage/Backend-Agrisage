using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Carts;

// FLOW_2 §4. One ACTIVE cart per Farmer per store, created on first use. Writes lock the Farmer's profile row so two
// requests never create two carts or lose a quantity. The cart promises no stock (reserved only at confirmation, F1.4).
public sealed class CartService(IAgriSageDbContext context, CurrentFarmer currentFarmer, IPriceResolver prices,
    IRowLockService locks, IDatabaseErrorClassifier databaseErrors, ICurrentUserService currentUser,
    IDateTimeProvider clock) : ICartService
{
    public async Task<CartResponse> GetAsync(CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        return await ViewAsync(farmer.FarmerProfileId, storeId, cancellationToken);
    }

    // A product + packaging already in the cart adds to that line's quantity (one line per pair).
    public async Task<CartResponse> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        var (farmer, storeId) = await BeginAsync(cancellationToken);
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockFarmerProfileAsync(farmer.FarmerProfileId, cancellationToken);

        var storeProduct = await context.StoreProducts.AsNoTracking().Include(sp => sp.Product)
            .FirstOrDefaultAsync(sp => sp.Id == request.StoreProductId && sp.StoreId == storeId, cancellationToken)
            ?? throw new BusinessRuleException("The store product does not exist in this store.");
        var packaging = await context.ProductPackagings.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductPackagingId, cancellationToken);
        if (OrderBuilder.GetSellabilityProblem(storeProduct, packaging) is { } problem)
        {
            throw new BusinessRuleException(problem);
        }

        var cart = await ActiveCartAsync(farmer.FarmerProfileId, storeId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart(storeId, farmer.FarmerProfileId);
            context.Carts.Add(cart);
        }

        var existing = cart.Items.SingleOrDefault(i => !i.IsDeleted
            && i.StoreProductId == storeProduct.Id && i.ProductPackagingId == packaging!.Id);
        var quantity = (existing?.Quantity ?? 0) + request.Quantity;
        if (quantity > OrderItemRequestValidator.MaxQuantity)
        {
            throw new BusinessRuleException($"A cart line can hold at most {OrderItemRequestValidator.MaxQuantity} units.");
        }

        var item = cart.SetItemQuantity(storeProduct, packaging!, quantity);
        if (existing is null)
        {
            // Track a child of an existing aggregate explicitly, like the Orders feature.
            context.CartItems.Add(item);
        }

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ViewAsync(farmer.FarmerProfileId, storeId, cancellationToken);
    }

    public async Task<CartResponse> UpdateItemAsync(Guid itemId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        var (farmer, storeId) = await BeginAsync(cancellationToken);
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockFarmerProfileAsync(farmer.FarmerProfileId, cancellationToken);

        var (cart, item) = await OwnItemAsync(farmer.FarmerProfileId, storeId, itemId, cancellationToken);
        var storeProduct = await context.StoreProducts.AsNoTracking().FirstAsync(sp => sp.Id == item.StoreProductId, cancellationToken);
        var packaging = await context.ProductPackagings.AsNoTracking().FirstAsync(p => p.Id == item.ProductPackagingId, cancellationToken);
        cart.SetItemQuantity(storeProduct, packaging, request.Quantity);

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ViewAsync(farmer.FarmerProfileId, storeId, cancellationToken);
    }

    public async Task<CartResponse> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var (farmer, storeId) = await BeginAsync(cancellationToken);
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockFarmerProfileAsync(farmer.FarmerProfileId, cancellationToken);

        var (cart, item) = await OwnItemAsync(farmer.FarmerProfileId, storeId, itemId, cancellationToken);
        cart.RemoveItem(item.Id, currentUser.UserId, clock.UtcNow);

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ViewAsync(farmer.FarmerProfileId, storeId, cancellationToken);
    }

    // The cart becomes ABANDONED; the next item starts a new cart. No cart → nothing to do.
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var (farmer, storeId) = await BeginAsync(cancellationToken);
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockFarmerProfileAsync(farmer.FarmerProfileId, cancellationToken);

        if (await ActiveCartAsync(farmer.FarmerProfileId, storeId, cancellationToken) is { } cart)
        {
            cart.MarkAbandoned();
            await SaveAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Tracked ACTIVE cart with its live lines (the soft-delete filter hides removed lines).
    public Task<Cart?> ActiveCartAsync(Guid farmerProfileId, Guid storeId, CancellationToken cancellationToken) =>
        context.Carts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.FarmerProfileId == farmerProfileId
                && c.Status == CartStatus.Active, cancellationToken);

    private async Task<CartResponse> ViewAsync(Guid farmerProfileId, Guid storeId, CancellationToken cancellationToken)
    {
        var cart = await context.Carts.AsNoTracking()
            .Where(c => c.StoreId == storeId && c.FarmerProfileId == farmerProfileId && c.Status == CartStatus.Active)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (cart is null)
        {
            return new CartResponse(null, [], 0m, null);
        }

        var items = await context.CartItems.AsNoTracking()
            .Where(i => i.CartId == cart.Id)
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Include(i => i.StoreProduct).ThenInclude(sp => sp.Product)
            .Include(i => i.ProductPackaging).ThenInclude(p => p.Unit)
            .ToListAsync(cancellationToken);

        // No price list applies (none active for the customer's group nor a walk-in default): show the cart with every
        // line NO_PRICE instead of failing; checkout reports the same problem as a 422.
        Guid? priceListId = null;
        IReadOnlyDictionary<PriceLine, decimal> suggested = new Dictionary<PriceLine, decimal>();
        try
        {
            priceListId = (await prices.GetContextAsync(farmerProfileId, clock.UtcNow, cancellationToken)).PriceListId;
        }
        catch (BusinessRuleException)
        {
        }

        if (priceListId is { } listId && items.Count > 0)
        {
            suggested = await prices.GetPricesAsync(listId,
                items.Select(i => new PriceLine(i.StoreProductId, i.ProductPackagingId)).Distinct().ToList(), cancellationToken);
        }

        var lines = items.Select(i =>
        {
            var reason = OrderBuilder.GetSellabilityProblem(i.StoreProduct, i.ProductPackaging) is not null
                ? CartUnavailableReason.NotSellable
                : suggested.ContainsKey(new PriceLine(i.StoreProductId, i.ProductPackagingId)) ? null : CartUnavailableReason.NoPrice;
            decimal? unitPrice = reason is null ? suggested[new PriceLine(i.StoreProductId, i.ProductPackagingId)] : null;

            return new CartItemResponse(i.Id, i.StoreProductId, i.ProductPackagingId, i.StoreProduct.Product.Sku,
                i.StoreProduct.Product.Name, i.ProductPackaging.PackagingName ?? i.ProductPackaging.Unit.Name,
                i.StoreProduct.Product.ImageUrl, i.Quantity, unitPrice, unitPrice * i.Quantity, reason is null, reason);
        }).ToList();

        return new CartResponse(cart.Id, lines, lines.Sum(l => l.LineTotalAmount ?? 0m), priceListId);
    }

    private async Task<(CurrentFarmer.Identity Farmer, Guid StoreId)> BeginAsync(CancellationToken cancellationToken) =>
        (await currentFarmer.GetAsync(cancellationToken), await ActiveStore.GetIdAsync(context, cancellationToken));

    // A line of someone else's cart, or of a cart that is no longer ACTIVE, is "not found".
    private async Task<(Cart Cart, CartItem Item)> OwnItemAsync(Guid farmerProfileId, Guid storeId, Guid itemId,
        CancellationToken cancellationToken)
    {
        var cart = await ActiveCartAsync(farmerProfileId, storeId, cancellationToken);
        // A line removed earlier in the same unit of work stays in the tracked collection, soft-deleted.
        var item = cart?.Items.SingleOrDefault(i => i.Id == itemId && !i.IsDeleted);

        return item is null ? throw new NotFoundException("Cart item", itemId) : (cart!, item);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("The cart changed at the same time; try again.");
        }
    }
}
