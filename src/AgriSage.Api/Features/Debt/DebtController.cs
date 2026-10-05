using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Debt;

[ApiController]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class DebtController(IDebtService service) : ControllerBase
{
    [HttpGet("api/debt-entries")]
    public Task<PagedResult<DebtEntryListItem>> Entries([FromQuery] DebtEntryListRequest request, CancellationToken token) => service.EntriesAsync(request, token);
    [HttpGet("api/debt-entries/{id:guid}")]
    public Task<DebtEntryResponse> Entry(Guid id, CancellationToken token) => service.EntryAsync(id, token);
    [HttpGet("api/debt-accounts")]
    public Task<PagedResult<DebtAccountListItem>> Accounts([FromQuery] DebtAccountListRequest request, CancellationToken token) => service.AccountsAsync(request, token);
    [HttpGet("api/customers/{farmerProfileId:guid}/debt")]
    public Task<DebtAccountResponse> Account(Guid farmerProfileId, CancellationToken token) => service.AccountAsync(farmerProfileId, token);
    [HttpGet("api/customers/{farmerProfileId:guid}/debt/entries")]
    public Task<PagedResult<DebtEntryListItem>> CustomerEntries(Guid farmerProfileId, [FromQuery] DebtEntryListRequest request, CancellationToken token) =>
        service.EntriesAsync(request with { FarmerProfileId = farmerProfileId }, token);
    [HttpGet("api/customers/{farmerProfileId:guid}/debt/transactions")]
    public Task<PagedResult<DebtTransactionResponse>> Transactions(Guid farmerProfileId, [FromQuery] DebtTransactionListRequest request, CancellationToken token) => service.TransactionsAsync(farmerProfileId, request, token);
    [HttpGet("api/customers/{farmerProfileId:guid}/debt/allocation-preview")]
    public Task<AllocationPreviewResponse> Preview(Guid farmerProfileId, [FromQuery] decimal amount, CancellationToken token) => service.PreviewAsync(farmerProfileId, amount, token);
    [HttpGet("api/debt-entries/{id:guid}/payments")]
    public async Task<IReadOnlyList<PaymentResponse>> Payments(Guid id, CancellationToken token) => (await service.EntryAsync(id, token)).Payments;
    [HttpGet("api/debt-entries/{id:guid}/ledger")]
    public async Task<IReadOnlyList<DebtTransactionResponse>> Ledger(Guid id, CancellationToken token) => (await service.EntryAsync(id, token)).Transactions;
    [HttpPost("api/debt-entries/{id:guid}/dispute")]
    public Task<DebtEntryResponse> Dispute(Guid id, DebtReasonRequest request, CancellationToken token) => service.ActionAsync(id, "DISPUTE", request.Reason, null, null, token);
    [HttpPost("api/debt-entries/{id:guid}/keep")]
    public Task<DebtEntryResponse> Keep(Guid id, DebtReasonRequest request, CancellationToken token) => service.ActionAsync(id, "KEEP", request.Reason, null, null, token);
    [HttpPost("api/debt-entries/{id:guid}/change-due-date")]
    public Task<DebtEntryResponse> DueDate(Guid id, DebtDueDateRequest request, CancellationToken token) => service.ActionAsync(id, "CHANGE_DUE_DATE", request.Reason, null, request.NewDueDate, token);
    [HttpPost("api/debt-entries/{id:guid}/adjust")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<DebtEntryResponse> Adjust(Guid id, DebtAdjustmentRequest request, CancellationToken token) => service.ActionAsync(id, "ADJUST", request.Reason, request.Amount, null, token);
    [HttpPost("api/debt-entries/{id:guid}/cancel")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<DebtEntryResponse> Cancel(Guid id, DebtReasonRequest request, CancellationToken token) => service.ActionAsync(id, "CANCEL", request.Reason, null, null, token);
    [HttpPost("api/customers/{farmerProfileId:guid}/debt/manual-entries")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Manual(Guid farmerProfileId, ManualDebtEntryRequest request, CancellationToken token)
    {
        var response = await service.ManualAsync(farmerProfileId, request, token);
        return CreatedAtAction(nameof(Entry), new { id = response.Id }, response);
    }
    [HttpGet("api/debt-entries/dashboard")]
    [Authorize(Roles = ApiRoles.Manage)]
    public Task<DebtDashboardResponse> Dashboard(CancellationToken token) => service.DashboardAsync(token);
}
