using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Invoices;

[ApiController, Route("api/invoices"), Authorize(Roles = ApiRoles.Operate)]
public sealed class InvoicesController(IInvoiceService service) : ControllerBase
{
    [HttpPost("/api/orders/{orderId:guid}/invoice")]
    public async Task<IActionResult> Issue(Guid orderId, CancellationToken token)
    {
        var result = await service.IssueAsync(orderId, token);
        return result.Created ? CreatedAtAction(nameof(Get), new { id = result.Invoice.Id }, result.Invoice) : Ok(result.Invoice);
    }
    [HttpGet]
    public Task<PagedResult<InvoiceListItem>> List([FromQuery] InvoiceListRequest request, CancellationToken token) => service.ListAsync(request, token);
    [HttpGet("{id:guid}")]
    public Task<InvoiceResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken token)
    {
        var pdf = await service.PdfAsync(id, token);
        Response.Headers.CacheControl = "private, no-store";
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
    [HttpGet("{id:guid}/emails")]
    public Task<PagedResult<InvoiceEmailResponse>> Emails(Guid id, [FromQuery] PaginationRequest request, CancellationToken token) => service.EmailsAsync(id, request, token);
    [HttpPost("{id:guid}/emails")]
    public Task<InvoiceEmailResponse> SendEmail(Guid id, InvoiceEmailRequest request, CancellationToken token) => service.SendEmailAsync(id, request, token);
}
