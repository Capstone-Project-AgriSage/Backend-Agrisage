namespace AgriSage.Application.Features.GoodsReceipts.Import;

// Header of an imported receipt (multipart form fields); the lines come from the sheet. Same header rules as a manual
// draft: ReceivedAt defaults to now, the receipt number and the receiver are server-owned.
public sealed record ReceiptImportRequest(
    Guid SupplierId,
    DateTimeOffset? ReceivedAt = null,
    string? SupplierInvoiceNumber = null,
    DateOnly? SupplierInvoiceDate = null,
    string? Note = null);

// The uploaded workbook. Content is read once; the service never stores the file (only its name).
public sealed record ReceiptImportFile(Stream Content, string FileName, long Length);

public sealed record ReceiptImportError(string Column, string Message);

// A sheet row as understood by the server; ids, product name and line total are set when the row could be resolved.
public sealed record ReceiptImportRow(
    int RowNumber,
    string? Sku,
    string? Packaging,
    Guid? StoreProductId,
    Guid? ProductPackagingId,
    string? ProductName,
    long? Quantity,
    decimal? UnitCost,
    string? LotNumber,
    DateOnly? ExpiryDate,
    DateOnly? ManufactureDate,
    decimal? LineTotalAmount,
    IReadOnlyList<ReceiptImportError> Errors);

public sealed record ReceiptImportPreviewResponse(
    string FileName,
    int RowCount,
    int ValidRowCount,
    decimal SubtotalAmount,
    IReadOnlyList<ReceiptImportRow> Rows);

public sealed record DownloadFile(byte[] Content, string FileName, string ContentType);

public interface IGoodsReceiptImportService
{
    // The template with the store's purchasable products on a reference sheet.
    Task<DownloadFile> GetTemplateAsync(CancellationToken cancellationToken);

    // Validates every row exactly like manual entry and saves nothing.
    Task<ReceiptImportPreviewResponse> PreviewAsync(
        ReceiptImportRequest request, ReceiptImportFile? file, CancellationToken cancellationToken);

    // Creates one DRAFT receipt (source EXCEL_TEMPLATE) when every row is valid; otherwise 422 with the row errors and
    // nothing is saved. The draft is then edited and confirmed with the normal endpoints.
    Task<GoodsReceiptResponse> ImportAsync(
        ReceiptImportRequest request, ReceiptImportFile? file, CancellationToken cancellationToken);
}
