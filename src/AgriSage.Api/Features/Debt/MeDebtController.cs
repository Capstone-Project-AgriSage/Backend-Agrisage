using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Debt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Debt;

[ApiController]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeDebtController(IDebtService service) : ControllerBase
{
    [HttpGet("api/me/debt")]
    public async Task<DebtAccountResponse> Account(CancellationToken token) => await service.AccountAsync(await service.OwnCustomerAsync(token), token);
    [HttpGet("api/me/debt-entries")]
    public async Task<PagedResult<DebtEntryListItem>> Entries([FromQuery] DebtEntryListRequest request, CancellationToken token) =>
        await service.EntriesAsync(request with { FarmerProfileId = await service.OwnCustomerAsync(token) }, token);
    [HttpGet("api/me/debt-entries/{id:guid}")]
    public async Task<DebtEntryResponse> Entry(Guid id, CancellationToken token)
    {
        await service.RequireOwnedEntryAsync(id, await service.OwnCustomerAsync(token), token);
        return await service.EntryAsync(id, token);
    }
    [HttpPost("api/me/debt-entries/{id:guid}/dispute")]
    public Task<DebtEntryResponse> Dispute(Guid id, DebtReasonRequest request, CancellationToken token) => service.DisputeOwnAsync(id, request.Reason, token);
    [HttpGet("api/me/debt/allocation-preview")]
    public async Task<AllocationPreviewResponse> Preview([FromQuery] decimal amount, CancellationToken token) =>
        await service.PreviewAsync(await service.OwnCustomerAsync(token), amount, token);
}
