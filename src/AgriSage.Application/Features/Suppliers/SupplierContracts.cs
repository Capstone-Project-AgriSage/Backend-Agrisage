using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Suppliers;

public sealed record SupplierRequest(
    string Name,
    string? Code = null,
    string? TaxCode = null,
    string? PhoneNumber = null,
    string? Email = null,
    string? ContactPerson = null,
    string? AddressLine = null,
    string? Ward = null,
    string? District = null,
    string? Province = null,
    string? Note = null);

public sealed record SupplierListRequest : PaginationRequest
{
    public bool? IsActive { get; init; }

    public string? Search { get; init; }
}

public sealed record SupplierResponse(
    Guid Id,
    string? Code,
    string Name,
    string? TaxCode,
    string? PhoneNumber,
    string? Email,
    string? ContactPerson,
    string? AddressLine,
    string? Ward,
    string? District,
    string? Province,
    string? Note,
    bool IsActive);

public interface ISupplierService
{
    Task<SupplierResponse> CreateAsync(SupplierRequest request, CancellationToken cancellationToken);

    Task<SupplierResponse> UpdateAsync(Guid id, SupplierRequest request, CancellationToken cancellationToken);

    Task<SupplierResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<SupplierResponse>> ListAsync(SupplierListRequest request, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken);

    // Soft delete; refused once any goods receipt used the supplier (deactivate it instead).
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
