using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.GoodsReceipts.Import;

// Goods receipt import from the AgriSage Excel template (task F4.3, design §22 "Excel workflow"): template → upload →
// preview (every row checked, nothing saved) → import (one DRAFT receipt, all rows or nothing) → normal edit/confirm.
// Rows are checked with the same ReceiptItemRules as manual entry; the draft is created by GoodsReceiptService.
public sealed class GoodsReceiptImportService(
    IAgriSageDbContext context,
    IDateTimeProvider clock,
    IReceiptSpreadsheet spreadsheet,
    GoodsReceiptService receipts) : IGoodsReceiptImportService
{
    private const int MaxTemplateProducts = 5000;

    public async Task<DownloadFile> GetTemplateAsync(CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var rows = await context.StoreProducts.AsNoTracking()
            .Where(sp => sp.StoreId == storeId && sp.IsActive && sp.Product.Status != ProductStatus.Discontinued)
            .SelectMany(sp => sp.Product.Packagings
                .Where(p => p.IsPurchaseUnit && p.Status == PackagingStatus.Active)
                .Select(p => new
                {
                    Sku = sp.StoreSku ?? sp.Product.Sku,
                    ProductName = sp.Product.Name,
                    Packaging = p.PackagingName ?? p.Unit.Name,
                    p.Barcode,
                    p.ConversionToBase,
                    BaseUnit = sp.Product.Packagings.Where(b => b.IsBaseUnit).Select(b => b.Unit.Name).FirstOrDefault(),
                    sp.Product.RequiresLotTracking,
                    sp.Product.RequiresExpiryDate
                }))
            .OrderBy(r => r.Sku).ThenBy(r => r.ConversionToBase)
            .Take(MaxTemplateProducts)
            .ToListAsync(cancellationToken);

        var products = rows.Select(r => new ReceiptTemplateProduct(
            r.Sku, r.ProductName, r.Packaging, r.Barcode, r.ConversionToBase, r.BaseUnit ?? "",
            r.RequiresLotTracking, r.RequiresExpiryDate)).ToList();

        return new DownloadFile(
            spreadsheet.CreateTemplate(products), ReceiptImportRules.TemplateFileName, ReceiptImportRules.ContentType);
    }

    public async Task<ReceiptImportPreviewResponse> PreviewAsync(
        ReceiptImportRequest request, ReceiptImportFile? file, CancellationToken cancellationToken)
    {
        var (fileName, rows) = await AnalyseAsync(request, file, cancellationToken);
        var valid = rows.Where(r => r.Row.Errors.Count == 0).ToList();

        return new ReceiptImportPreviewResponse(
            fileName, rows.Count, valid.Count, valid.Sum(r => r.Row.LineTotalAmount ?? 0m), rows.Select(r => r.Row).ToList());
    }

    public async Task<GoodsReceiptResponse> ImportAsync(
        ReceiptImportRequest request, ReceiptImportFile? file, CancellationToken cancellationToken)
    {
        var (fileName, rows) = await AnalyseAsync(request, file, cancellationToken);
        var invalid = rows.Where(r => r.Row.Errors.Count > 0).ToList();
        if (invalid.Count > 0)
        {
            throw new BusinessRuleException(
                $"{invalid.Count} of {rows.Count} rows are invalid; nothing was imported. Fix the file and upload it again.",
                invalid.ToDictionary(
                    r => $"row {r.Row.RowNumber}",
                    r => r.Row.Errors.Select(e => $"{e.Column}: {e.Message}").ToArray()));
        }

        var draft = new CreateGoodsReceiptRequest(
            request.SupplierId, request.ReceivedAt, request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.Note,
            rows.Select(r => r.Item!).ToList());

        return await receipts.CreateAsync(draft, GoodsReceiptSourceType.ExcelTemplate, fileName, cancellationToken);
    }

    private sealed record AnalysedRow(ReceiptImportRow Row, GoodsReceiptItemRequest? Item);

    private async Task<(string FileName, List<AnalysedRow> Rows)> AnalyseAsync(
        ReceiptImportRequest request, ReceiptImportFile? file, CancellationToken cancellationToken)
    {
        var sheetRows = await ReadAsync(file, cancellationToken);

        // Header first: an unknown or inactive supplier fails the whole file like a manual draft (404 / 422).
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        await receipts.EnsureSupplierUsableAsync(storeId, request.SupplierId, cancellationToken);
        var now = clock.UtcNow;
        GoodsReceiptService.EnsureNotFuture(request.ReceivedAt ?? now, now);
        var today = BusinessCalendar.Today(now);

        var catalog = await LoadCatalogAsync(storeId, sheetRows, cancellationToken);

        return (ReceiptImportValues.FileName(file!.FileName), sheetRows.Select(row => Analyse(row, catalog, today)).ToList());
    }

    private async Task<IReadOnlyList<ReceiptSheetRow>> ReadAsync(ReceiptImportFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw FileError("Upload the filled template as an .xlsx file in the field \"file\".");
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw FileError("Only .xlsx files (the AgriSage template) are accepted.");
        }

        if (file.Length > ReceiptImportRules.MaxFileBytes)
        {
            throw FileError("The file is larger than 2 MB.");
        }

        // Copied with a hard limit: the declared length is not trusted.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await file.Content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > ReceiptImportRules.MaxFileBytes)
            {
                throw FileError("The file is larger than 2 MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        var rows = spreadsheet.Read(buffer, ReceiptImportRules.MaxRows);

        return rows.Count > 0 ? rows : throw FileError("The receipt sheet has no data rows.");
    }

    // Store products whose store SKU or product SKU appears in the file, with their packagings, in one query.
    private async Task<List<StoreProduct>> LoadCatalogAsync(
        Guid storeId, IReadOnlyList<ReceiptSheetRow> rows, CancellationToken cancellationToken)
    {
        var keys = rows.Select(r => Key(r.Sku)).OfType<string>().Distinct().ToList();

        return await context.StoreProducts.AsNoTracking()
            .Include(sp => sp.Product).ThenInclude(p => p.Packagings).ThenInclude(p => p.Unit)
            .Where(sp => sp.StoreId == storeId
                && ((sp.StoreSku != null && keys.Contains(sp.StoreSku.Trim().ToLower())) || keys.Contains(sp.Product.Sku.ToLower())))
            .ToListAsync(cancellationToken);
    }

    private static AnalysedRow Analyse(ReceiptSheetRow row, List<StoreProduct> catalog, DateOnly today)
    {
        var errors = new List<ReceiptImportError>();
        void Error(string column, string message) => errors.Add(new ReceiptImportError(column, message));

        long? quantity = null;
        if (string.IsNullOrWhiteSpace(row.Quantity))
        {
            Error(ReceiptSheetColumns.Quantity, "Required.");
        }
        else if (ReceiptImportValues.TryParseQuantity(row.Quantity, out var parsedQuantity))
        {
            quantity = parsedQuantity;
        }
        else
        {
            Error(ReceiptSheetColumns.Quantity, "Must be a whole number of packages, at least 1.");
        }

        decimal? unitCost = null;
        if (string.IsNullOrWhiteSpace(row.UnitCost))
        {
            Error(ReceiptSheetColumns.UnitCost, "Required.");
        }
        else if (ReceiptImportValues.TryParseMoney(row.UnitCost, out var parsedCost))
        {
            unitCost = parsedCost;
        }
        else
        {
            Error(ReceiptSheetColumns.UnitCost, "Must be a number ≥ 0 with at most 2 decimals (dot as decimal separator).");
        }

        var lotNumber = Texts.Clean(row.LotNumber);
        if (lotNumber?.Length > ReceiptImportRules.MaxLotNumberLength)
        {
            Error(ReceiptSheetColumns.LotNumber, $"At most {ReceiptImportRules.MaxLotNumberLength} characters.");
        }

        var expiryDate = Date(row.ExpiryDate, ReceiptSheetColumns.ExpiryDate, Error, out var expiryValid);
        var manufactureDate = Date(row.ManufactureDate, ReceiptSheetColumns.ManufactureDate, Error, out var manufactureValid);

        var storeProduct = FindStoreProduct(row.Sku, catalog, Error);
        var packaging = storeProduct is null ? null : FindPackaging(row.Packaging, storeProduct.Product, Error);

        if (storeProduct is not null && packaging is not null && expiryValid && manufactureValid)
        {
            var violation = ReceiptItemRules.Check(storeProduct.Product, packaging, lotNumber, manufactureDate, expiryDate, today);
            if (violation is not null)
            {
                Error(ColumnOf(violation.Field), violation.Message);
            }
        }

        var result = new ReceiptImportRow(
            row.RowNumber, Texts.Clean(row.Sku), Texts.Clean(row.Packaging), storeProduct?.Id, packaging?.Id,
            storeProduct?.Product.Name, quantity, unitCost, lotNumber, expiryDate, manufactureDate,
            quantity * unitCost, errors);

        var item = errors.Count == 0
            ? new GoodsReceiptItemRequest(
                storeProduct!.Id, packaging!.Id, quantity!.Value, unitCost!.Value, lotNumber, manufactureDate, expiryDate)
            : null;

        return new AnalysedRow(result, item);
    }

    private static DateOnly? Date(string? text, string column, Action<string, string> error, out bool valid)
    {
        valid = true;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (ReceiptImportValues.TryParseDate(text, out var date))
        {
            return date;
        }

        valid = false;
        error(column, "Use a date cell or the text yyyy-MM-dd / dd/MM/yyyy.");
        return null;
    }

    // Store SKU first (what the template lists when the store set one), then the product SKU.
    private static StoreProduct? FindStoreProduct(string? sku, List<StoreProduct> catalog, Action<string, string> error)
    {
        var key = Key(sku);
        if (key is null)
        {
            error(ReceiptSheetColumns.Sku, "Required.");
            return null;
        }

        var byStoreSku = catalog.Where(sp => Key(sp.StoreSku) == key).ToList();
        if (byStoreSku.Count > 1)
        {
            error(ReceiptSheetColumns.Sku, "Several store products use this store SKU; fix the store SKUs first.");
            return null;
        }

        var match = byStoreSku.SingleOrDefault() ?? catalog.SingleOrDefault(sp => Key(sp.Product.Sku) == key);
        if (match is null)
        {
            error(ReceiptSheetColumns.Sku, "No product of this store has this SKU (see the Products sheet of the template).");
        }

        return match;
    }

    // Barcode, packaging name or unit name/code; when several match, the only ACTIVE purchase packaging among them wins.
    private static ProductPackaging? FindPackaging(string? text, Product product, Action<string, string> error)
    {
        var key = Key(text);
        if (key is null)
        {
            error(ReceiptSheetColumns.Packaging, "Required.");
            return null;
        }

        var matches = product.Packagings
            .Where(p => !p.IsDeleted
                && (Key(p.Barcode) == key || Key(p.PackagingName) == key || Key(p.Unit.Name) == key || Key(p.Unit.Code) == key))
            .ToList();
        if (matches.Count > 1)
        {
            var purchasable = matches.Where(p => p.IsPurchaseUnit && p.Status == PackagingStatus.Active).ToList();
            if (purchasable.Count == 1)
            {
                return purchasable[0];
            }
        }

        switch (matches.Count)
        {
            case 1:
                return matches[0];
            case 0:
                error(ReceiptSheetColumns.Packaging, "No packaging of this product matches (use Packaging or Barcode from the Products sheet).");
                return null;
            default:
                error(ReceiptSheetColumns.Packaging, "Several packagings of this product match; use the barcode.");
                return null;
        }
    }

    private static string ColumnOf(ReceiptItemField field) => field switch
    {
        ReceiptItemField.Product => ReceiptSheetColumns.Sku,
        ReceiptItemField.Packaging => ReceiptSheetColumns.Packaging,
        ReceiptItemField.LotNumber => ReceiptSheetColumns.LotNumber,
        ReceiptItemField.ExpiryDate => ReceiptSheetColumns.ExpiryDate,
        ReceiptItemField.ManufacturingDate => ReceiptSheetColumns.ManufactureDate,
        _ => ReceiptSheetColumns.Row
    };

    private static string? Key(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static ValidationException FileError(string message) =>
        new(new Dictionary<string, string[]> { ["file"] = [message] });
}
