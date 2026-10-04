using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Customers;

public sealed record CustomerGroupRequest(string Code, string Name, string? Description = null, int Priority = 0);
public sealed record UpdateCustomerGroupRequest(string Name, string? Description = null, int Priority = 0);
public sealed record GroupCreditTierRequest(Guid? CreditTierId);
public sealed record GroupPriceListRequest(Guid PriceListId, DateTimeOffset? EffectiveFrom = null);
public sealed record CustomerGroupListRequest : PaginationRequest
{
    public bool? IsActive { get; init; }
    public string? Search { get; init; }
}
public sealed record CustomerGroupResponse(Guid Id, string Code, string Name, string? Description, int Priority,
    bool IsDefault, bool IsActive, long MemberCount, CustomerReference? CurrentPriceList,
    CustomerReference? DefaultCreditTier, DateTimeOffset CreatedAt);
public sealed record GroupPriceListResponse(Guid Id, CustomerReference PriceList, string Status,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);

// PostgreSQL partial uniqueness is immediate: pre-clear the old flag before EF updates the new default.
// Application still changes both Domain entities and saves them through the normal audit interceptor.
// This preparatory persistence step participates in the caller's transaction; it never saves or commits.
public interface ICustomerGroupDefaultSwitcher
{
    Task ClearPreviousAsync(Guid storeId, Guid groupId, CancellationToken cancellationToken);
}

public interface ICustomerGroupService
{
    Task<PagedResult<CustomerGroupResponse>> ListAsync(CustomerGroupListRequest request, CancellationToken cancellationToken);
    Task<CustomerGroupResponse> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerGroupResponse> CreateAsync(CustomerGroupRequest request, CancellationToken cancellationToken);
    Task<CustomerGroupResponse> UpdateAsync(Guid id, UpdateCustomerGroupRequest request, CancellationToken cancellationToken);
    Task<CustomerGroupResponse> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken);
    Task<CustomerGroupResponse> SetDefaultAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerGroupResponse> SetCreditTierAsync(Guid id, GroupCreditTierRequest request, CancellationToken cancellationToken);
    Task<CustomerGroupResponse> SetPriceListAsync(Guid id, GroupPriceListRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupPriceListResponse>> PriceListsAsync(Guid id, CancellationToken cancellationToken);
}
