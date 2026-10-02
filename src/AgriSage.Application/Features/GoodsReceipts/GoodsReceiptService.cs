using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.GoodsReceipts.Entities;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.GoodsReceipts;

// Draft handling of goods receipts. Only DRAFT receipts change; confirmation is GoodsReceiptConfirmer.
public sealed class GoodsReceiptService(
    IAgriSageDbContext context,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    GoodsReceiptConfirmer confirmer) : IGoodsReceiptService
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    public async Task<GoodsReceiptResponse> CreateAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var actorId = ActorId();
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        await EnsureSupplierUsableAsync(storeId, request.SupplierId, cancellationToken);

        var now = clock.UtcNow;
        var receivedAt = request.ReceivedAt ?? now;
        EnsureNotFuture(receivedAt, now);
        var today = BusinessCalendar.Today(now);

        var number = await DocumentNumbers.NextReceiptNumberAsync(context, storeId, today, cancellationToken);
        var receipt = new GoodsReceipt(
            storeId, request.SupplierId, number, receivedAt, actorId, GoodsReceiptSourceType.Manual,
            supplierInvoiceNumber: Texts.Clean(request.SupplierInvoiceNumber),
            supplierInvoiceDate: request.SupplierInvoiceDate,
            note: Texts.Clean(request.Note));

        foreach (var item in request.Items ?? [])
        {
            await AddItemAsync(receipt, storeId, item, today, cancellationToken);
        }

        context.GoodsReceipts.Add(receipt);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("The receipt number was taken by another request; please try again.");
        }

        return await GetAsync(receipt.Id, cancellationToken);
    }

    public async Task<GoodsReceiptResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var receipt = await context.GoodsReceipts.AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Items).ThenInclude(i => i.StoreProduct).ThenInclude(sp => sp.Product)
            .Include(r => r.Items).ThenInclude(i => i.ProductPackaging).ThenInclude(p => p.Unit)
            .FirstOrDefaultAsync(r => r.Id == id && r.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Goods receipt", id);

        var movementId = await context.StockMovements.AsNoTracking()
            .Where(m => m.GoodsReceiptId == id && m.MovementType == StockMovementType.StockIn)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken);

        var items = receipt.Items.OrderBy(i => i.Id).Select(i => new GoodsReceiptItemResponse(
            i.Id, i.StoreProductId, i.StoreProduct.Product.Sku, i.StoreProduct.Product.Name, i.ProductPackagingId,
            i.ProductPackaging.Unit.Name, i.ProductPackaging.PackagingName, i.ReceivedQuantity, i.ConversionToBaseSnapshot,
            i.BaseQuantity, i.PurchaseUnitCost, i.BaseUnitCost, i.LineTotalAmount, i.SupplierLotNumber, i.ManufacturingDate,
            i.ExpiryDate, i.InventoryLotId, i.Note)).ToList();

        return new GoodsReceiptResponse(
            receipt.Id, receipt.ReceiptNumber, receipt.SupplierId, receipt.Supplier.Name, receipt.SupplierInvoiceNumber,
            receipt.SupplierInvoiceDate, receipt.ReceivedAt, receipt.ReceivedBy, EnumText.Format(receipt.SourceType),
            EnumText.Format(receipt.Status), receipt.SubtotalAmount, receipt.TotalAmount, receipt.Note, receipt.ConfirmedAt,
            receipt.ConfirmedBy, receipt.CancelledAt, receipt.CancelledBy, receipt.CancelReason, movementId, items);
    }

    public async Task<PagedResult<GoodsReceiptListItem>> ListAsync(GoodsReceiptListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.GoodsReceipts.AsNoTracking().Where(r => r.StoreId == storeId);

        if (EnumText.TryParse<GoodsReceiptStatus>(request.Status, out var status))
        {
            query = query.Where(r => r.Status == status);
        }

        if (request.SupplierId is not null)
        {
            query = query.Where(r => r.SupplierId == request.SupplierId);
        }

        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(r => r.ReceivedAt >= start);
        }

        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(r => r.ReceivedAt < end);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(r => r.ReceiptNumber.ToLower().Contains(term)
                || (r.SupplierInvoiceNumber != null && r.SupplierInvoiceNumber.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(r => new
            {
                r.Id, r.ReceiptNumber, r.SupplierId, SupplierName = r.Supplier.Name, r.SupplierInvoiceNumber,
                r.ReceivedAt, r.Status, r.TotalAmount, ItemCount = r.Items.Count
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new GoodsReceiptListItem(
            r.Id, r.ReceiptNumber, r.SupplierId, r.SupplierName, r.SupplierInvoiceNumber, r.ReceivedAt,
            EnumText.Format(r.Status), r.TotalAmount, r.ItemCount)).ToList();

        return new PagedResult<GoodsReceiptListItem>(items, request.Page, request.PageSize, total);
    }

    public async Task<GoodsReceiptResponse> UpdateHeaderAsync(Guid id, UpdateGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var actorId = ActorId();
        var receipt = await LoadAsync(id, cancellationToken);
        await EnsureSupplierUsableAsync(receipt.StoreId, request.SupplierId, cancellationToken);
        EnsureNotFuture(request.ReceivedAt, clock.UtcNow);

        receipt.UpdateHeader(
            request.SupplierId, Texts.Clean(request.SupplierInvoiceNumber), request.SupplierInvoiceDate,
            request.ReceivedAt, actorId, Texts.Clean(request.Note));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<GoodsReceiptResponse> AddItemAsync(Guid id, GoodsReceiptItemRequest request, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(id, cancellationToken);

        await AddItemAsync(receipt, receipt.StoreId, request, BusinessCalendar.Today(clock.UtcNow), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<GoodsReceiptResponse> UpdateItemAsync(
        Guid id, Guid itemId, UpdateGoodsReceiptItemRequest request, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(id, cancellationToken);
        var item = receipt.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Goods receipt item", itemId);

        Check(item.StoreProduct.Product, item.ProductPackaging, request.SupplierLotNumber, request.ManufacturingDate,
            request.ExpiryDate, BusinessCalendar.Today(clock.UtcNow));
        receipt.UpdateItem(
            itemId, request.ReceivedQuantity, request.PurchaseUnitCost, Texts.Clean(request.SupplierLotNumber),
            request.ManufacturingDate, request.ExpiryDate, Texts.Clean(request.Note));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<GoodsReceiptResponse> RemoveItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(id, cancellationToken);
        if (receipt.Items.All(i => i.Id != itemId))
        {
            throw new NotFoundException("Goods receipt item", itemId);
        }

        receipt.RemoveItem(itemId, currentUser.UserId, clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<GoodsReceiptResponse> CancelAsync(Guid id, CancelGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var actorId = ActorId();
        var receipt = await LoadAsync(id, cancellationToken);

        receipt.Cancel(actorId, clock.UtcNow, Texts.Clean(request.Reason));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(id, cancellationToken);

        context.GoodsReceipts.Remove(receipt);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<GoodsReceiptResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        await confirmer.ConfirmAsync(id, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    private async Task<GoodsReceipt> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        return await context.GoodsReceipts
            .Include(r => r.Items).ThenInclude(i => i.StoreProduct).ThenInclude(sp => sp.Product)
            .Include(r => r.Items).ThenInclude(i => i.ProductPackaging)
            .FirstOrDefaultAsync(r => r.Id == id && r.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Goods receipt", id);
    }

    private async Task AddItemAsync(
        GoodsReceipt receipt, Guid storeId, GoodsReceiptItemRequest request, DateOnly today, CancellationToken cancellationToken)
    {
        var storeProduct = await context.StoreProducts.Include(sp => sp.Product)
            .FirstOrDefaultAsync(sp => sp.Id == request.StoreProductId && sp.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Store product", request.StoreProductId);
        var packaging = await context.ProductPackagings
            .FirstOrDefaultAsync(p => p.Id == request.ProductPackagingId, cancellationToken)
            ?? throw new NotFoundException("Product packaging", request.ProductPackagingId);

        Check(storeProduct.Product, packaging, request.SupplierLotNumber, request.ManufacturingDate, request.ExpiryDate, today);
        receipt.AddItem(
            storeProduct, packaging, request.ReceivedQuantity, request.PurchaseUnitCost, Texts.Clean(request.SupplierLotNumber),
            request.ManufacturingDate, request.ExpiryDate, Texts.Clean(request.Note));
    }

    private static void Check(
        AgriSage.Domain.Features.Products.Entities.Product product,
        AgriSage.Domain.Features.Products.Entities.ProductPackaging packaging,
        string? lotNumber, DateOnly? manufacturingDate, DateOnly? expiryDate, DateOnly today)
    {
        var violation = ReceiptItemRules.GetViolation(product, packaging, lotNumber, manufacturingDate, expiryDate, today);
        if (violation is not null)
        {
            throw new BusinessRuleException(violation);
        }
    }

    private async Task EnsureSupplierUsableAsync(Guid storeId, Guid supplierId, CancellationToken cancellationToken)
    {
        var supplier = await context.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == supplierId && s.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Supplier", supplierId);

        if (!supplier.IsActive)
        {
            throw new BusinessRuleException("The supplier is not active.");
        }
    }

    private static void EnsureNotFuture(DateTimeOffset receivedAt, DateTimeOffset now)
    {
        if (receivedAt > now + ClockTolerance)
        {
            throw new BusinessRuleException("The received time cannot be in the future.");
        }
    }

    private Guid ActorId() => currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
}
