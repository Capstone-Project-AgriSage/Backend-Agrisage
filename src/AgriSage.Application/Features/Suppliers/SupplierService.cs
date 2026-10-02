using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Suppliers.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Suppliers;

public sealed class SupplierService(IAgriSageDbContext context, IDatabaseErrorClassifier databaseErrors) : ISupplierService
{
    public async Task<SupplierResponse> CreateAsync(SupplierRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        await EnsureCodeIsFreeAsync(storeId, Texts.Clean(request.Code), null, cancellationToken);

        var supplier = new Supplier(
            storeId, request.Name.Trim(), Texts.Clean(request.Code), Texts.Clean(request.TaxCode), Texts.Clean(request.PhoneNumber),
            ContactNormalizer.NormalizeEmail(request.Email), Texts.Clean(request.ContactPerson), Texts.Clean(request.AddressLine),
            Texts.Clean(request.Ward), Texts.Clean(request.District), Texts.Clean(request.Province), Texts.Clean(request.Note));
        context.Suppliers.Add(supplier);
        await SaveAsync(cancellationToken);

        return ToResponse(supplier);
    }

    public async Task<SupplierResponse> UpdateAsync(Guid id, SupplierRequest request, CancellationToken cancellationToken)
    {
        var supplier = await FindAsync(id, cancellationToken);
        await EnsureCodeIsFreeAsync(supplier.StoreId, Texts.Clean(request.Code), id, cancellationToken);

        supplier.UpdateDetails(
            request.Name.Trim(), Texts.Clean(request.Code), Texts.Clean(request.TaxCode), Texts.Clean(request.PhoneNumber),
            ContactNormalizer.NormalizeEmail(request.Email), Texts.Clean(request.ContactPerson), Texts.Clean(request.AddressLine),
            Texts.Clean(request.Ward), Texts.Clean(request.District), Texts.Clean(request.Province), Texts.Clean(request.Note));
        await SaveAsync(cancellationToken);

        return ToResponse(supplier);
    }

    public async Task<SupplierResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await FindAsync(id, cancellationToken, tracking: false));

    public async Task<PagedResult<SupplierResponse>> ListAsync(SupplierListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.Suppliers.AsNoTracking().Where(s => s.StoreId == storeId);

        if (request.IsActive is not null)
        {
            query = query.Where(s => s.IsActive == request.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term)
                || (s.Code != null && s.Code.ToLower().Contains(term))
                || (s.TaxCode != null && s.TaxCode.Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(s => s.Name).ThenBy(s => s.Id).Skip(request.Skip).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierResponse>(items.Select(ToResponse).ToList(), request.Page, request.PageSize, total);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var supplier = await FindAsync(id, cancellationToken);
        if (active)
        {
            supplier.Activate();
        }
        else
        {
            supplier.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await FindAsync(id, cancellationToken);
        if (await context.GoodsReceipts.IgnoreQueryFilters().AnyAsync(r => r.SupplierId == id, cancellationToken))
        {
            throw new ConflictException("Goods receipts exist for this supplier; deactivate it instead.");
        }

        context.Suppliers.Remove(supplier);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Supplier> FindAsync(Guid id, CancellationToken cancellationToken, bool tracking = true)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = tracking ? context.Suppliers.AsQueryable() : context.Suppliers.AsNoTracking();

        return await query.FirstOrDefaultAsync(s => s.Id == id && s.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Supplier", id);
    }

    private async Task EnsureCodeIsFreeAsync(Guid storeId, string? code, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (code is null)
        {
            return;
        }

        var lower = code.ToLower();
        if (await context.Suppliers.AnyAsync(
                s => s.StoreId == storeId && s.Id != exceptId && s.Code != null && s.Code.ToLower() == lower, cancellationToken))
        {
            throw new ConflictException($"A supplier with code '{code}' already exists.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("A supplier with the same code already exists.");
        }
    }

    private static SupplierResponse ToResponse(Supplier s) => new(
        s.Id, s.Code, s.Name, s.TaxCode, s.PhoneNumber, s.Email, s.ContactPerson, s.AddressLine, s.Ward, s.District,
        s.Province, s.Note, s.IsActive);
}
