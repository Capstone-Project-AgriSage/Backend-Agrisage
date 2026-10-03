using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.GoodsReceipts;
using AgriSage.Application.Features.GoodsReceipts.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriSage.Api.Features.GoodsReceipts;

// Admin, Store Owner and Sales run the whole receiving flow, including confirmation.
[ApiController]
[Route("api/goods-receipts")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class GoodsReceiptsController(IGoodsReceiptService service, IGoodsReceiptImportService import) : ControllerBase
{
    private const long ImportRequestLimit = ReceiptImportRules.MaxFileBytes + 512 * 1024;

    // Excel import (FLOW_4 §5): template with the store's purchasable products, preview (nothing saved), import (DRAFT).
    [HttpGet("import-template")]
    [Produces(ReceiptImportRules.ContentType)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ReceiptImportRules.ContentType)]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken)
    {
        var template = await import.GetTemplateAsync(cancellationToken);

        return File(template.Content, template.ContentType, template.FileName);
    }

    // multipart/form-data: "file" (.xlsx ≤ 2 MB, ≤ 500 rows) + supplierId, receivedAt?, supplierInvoiceNumber?,
    // supplierInvoiceDate?, note?. Every row is checked like manual entry; per-row errors come back in the response.
    [HttpPost("import/preview")]
    [EnableRateLimiting(RateLimitingExtensions.UploadPolicy)]
    [RequestSizeLimit(ImportRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportRequestLimit)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ReceiptImportPreviewResponse>> PreviewImport(
        [FromForm] ReceiptImportRequest request, IFormFile? file, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream();

        return await import.PreviewAsync(request, ImportFile(file, content), cancellationToken);
    }

    // Same form as the preview. Creates one DRAFT receipt (source EXCEL_TEMPLATE) when every row is valid; otherwise
    // 422 with `errors` per row and nothing is saved.
    [HttpPost("import")]
    [EnableRateLimiting(RateLimitingExtensions.UploadPolicy)]
    [RequestSizeLimit(ImportRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportRequestLimit)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<GoodsReceiptResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Import(
        [FromForm] ReceiptImportRequest request, IFormFile? file, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream();
        var response = await import.ImportAsync(request, ImportFile(file, content), cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    private static ReceiptImportFile? ImportFile(IFormFile? file, Stream? content) =>
        file is null || content is null ? null : new ReceiptImportFile(content, file.FileName, file.Length);

    [HttpGet]
    public async Task<ActionResult<PagedResult<GoodsReceiptListItem>>> List(
        [FromQuery] GoodsReceiptListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<GoodsReceiptResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> UpdateHeader(
        Guid id, UpdateGoodsReceiptRequest request, CancellationToken cancellationToken) =>
        await service.UpdateHeaderAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<GoodsReceiptResponse>> AddItem(
        Guid id, GoodsReceiptItemRequest request, CancellationToken cancellationToken) =>
        await service.AddItemAsync(id, request, cancellationToken);

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> UpdateItem(
        Guid id, Guid itemId, UpdateGoodsReceiptItemRequest request, CancellationToken cancellationToken) =>
        await service.UpdateItemAsync(id, itemId, request, cancellationToken);

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<GoodsReceiptResponse>> RemoveItem(
        Guid id, Guid itemId, CancellationToken cancellationToken) =>
        await service.RemoveItemAsync(id, itemId, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<GoodsReceiptResponse>> Cancel(
        Guid id, CancelGoodsReceiptRequest request, CancellationToken cancellationToken) =>
        await service.CancelAsync(id, request, cancellationToken);

    // Soft delete of a DRAFT receipt.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);

        return NoContent();
    }

    // Creates/finds lots, adds stock at cost, posts STOCK_IN, marks the receipt CONFIRMED.
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<GoodsReceiptResponse>> Confirm(Guid id, CancellationToken cancellationToken) =>
        await service.ConfirmAsync(id, cancellationToken);
}
