using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Returns;

public sealed class MySalesReturnService(IAgriSageDbContext context, ICurrentUserService currentUser,
    SalesReturnService service, SalesReturnQueries queries) : IMySalesReturnService
{
    public async Task<ReturnableResponse> ReturnableAsync(Guid orderId, CancellationToken token) =>
        await queries.ReturnableAsync(orderId, await FarmerAsync(token), token);
    public async Task<SalesReturnResponse> CreateAsync(CreateReturnRequest request, CancellationToken token) =>
        await service.CreateForAsync(request, await FarmerAsync(token), token);
    public async Task<SalesReturnResponse> GetAsync(Guid id, CancellationToken token) =>
        await queries.GetAsync(id, await FarmerAsync(token), token);
    public async Task<PagedResult<SalesReturnListItem>> ListAsync(PaginationRequest request, CancellationToken token) =>
        await queries.ListAsync(new() { Page = request.Page, PageSize = request.PageSize }, await FarmerAsync(token), token);
    public async Task<SalesReturnResponse> CancelAsync(Guid id, ReturnReasonRequest request, CancellationToken token) =>
        await service.CancelForAsync(id, request, await FarmerAsync(token), token);
    private async Task<Guid> FarmerAsync(CancellationToken token)
    {
        var user = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        if (currentUser.Role != "FARMER") throw new ForbiddenException();
        return await context.FarmerProfiles.AsNoTracking().Where(f => f.UserId == user).Select(f => (Guid?)f.Id).SingleOrDefaultAsync(token)
            ?? throw new ForbiddenException("This account has no customer profile.");
    }
}
