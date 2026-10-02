using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using AgriSage.Domain.Features.Products.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Products.Services;

public sealed class BrandService(IAgriSageDbContext context, IDatabaseErrorClassifier databaseErrors) : IBrandService
{
    public async Task<BrandResponse> CreateAsync(BrandRequest request, CancellationToken cancellationToken)
    {
        await EnsureNameIsFreeAsync(request.Name.Trim(), null, cancellationToken);

        var brand = new Brand(request.Name.Trim(), Texts.Clean(request.Code), Texts.Clean(request.Description), Texts.Clean(request.LogoUrl));
        context.Brands.Add(brand);
        await SaveAsync(cancellationToken);

        return ToResponse(brand);
    }

    public async Task<BrandResponse> UpdateAsync(Guid id, BrandRequest request, CancellationToken cancellationToken)
    {
        var brand = await FindAsync(id, cancellationToken);
        await EnsureNameIsFreeAsync(request.Name.Trim(), id, cancellationToken);

        brand.Update(request.Name.Trim(), Texts.Clean(request.Code), Texts.Clean(request.Description), Texts.Clean(request.LogoUrl));
        await SaveAsync(cancellationToken);

        return ToResponse(brand);
    }

    public async Task<BrandResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await FindAsync(id, cancellationToken, tracking: false));

    public async Task<PagedResult<BrandResponse>> ListAsync(BrandListRequest request, CancellationToken cancellationToken)
    {
        var query = context.Brands.AsNoTracking().AsQueryable();

        if (request.IsActive is not null)
        {
            query = query.Where(b => b.IsActive == request.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(b => b.Name.ToLower().Contains(term) || (b.Code != null && b.Code.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(b => b.Name).ThenBy(b => b.Id)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<BrandResponse>(items.Select(ToResponse).ToList(), request.Page, request.PageSize, total);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var brand = await FindAsync(id, cancellationToken);
        if (active)
        {
            brand.Activate();
        }
        else
        {
            brand.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var brand = await FindAsync(id, cancellationToken);
        if (await context.Products.AnyAsync(p => p.BrandId == id, cancellationToken))
        {
            throw new ConflictException("Products use this brand; change their brand first or deactivate the brand.");
        }

        context.Brands.Remove(brand);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Brand> FindAsync(Guid id, CancellationToken cancellationToken, bool tracking = true)
    {
        var query = tracking ? context.Brands.AsQueryable() : context.Brands.AsNoTracking();

        return await query.FirstOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw new NotFoundException("Brand", id);
    }

    private async Task EnsureNameIsFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lower = name.ToLower();
        if (await context.Brands.AnyAsync(b => b.Id != exceptId && b.Name.ToLower() == lower, cancellationToken))
        {
            throw new ConflictException($"A brand named '{name}' already exists.");
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
            throw new ConflictException("A brand with the same name already exists.");
        }
    }

    private static BrandResponse ToResponse(Brand b) => new(b.Id, b.Code, b.Name, b.Description, b.LogoUrl, b.IsActive);
}

public sealed class ActiveIngredientService(IAgriSageDbContext context) : IActiveIngredientService
{
    public async Task<ActiveIngredientResponse> CreateAsync(ActiveIngredientRequest request, CancellationToken cancellationToken)
    {
        await EnsureNameIsFreeAsync(request.Name.Trim(), null, cancellationToken);

        var ingredient = new ActiveIngredient(request.Name.Trim(), Texts.Clean(request.Code), Texts.Clean(request.Description));
        context.ActiveIngredients.Add(ingredient);
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(ingredient);
    }

    public async Task<ActiveIngredientResponse> UpdateAsync(Guid id, ActiveIngredientRequest request, CancellationToken cancellationToken)
    {
        var ingredient = await FindAsync(id, cancellationToken);
        await EnsureNameIsFreeAsync(request.Name.Trim(), id, cancellationToken);

        ingredient.Update(request.Name.Trim(), Texts.Clean(request.Code), Texts.Clean(request.Description));
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(ingredient);
    }

    public async Task<ActiveIngredientResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await FindAsync(id, cancellationToken, tracking: false));

    public async Task<PagedResult<ActiveIngredientResponse>> ListAsync(
        ActiveIngredientListRequest request,
        CancellationToken cancellationToken)
    {
        var query = context.ActiveIngredients.AsNoTracking().AsQueryable();

        if (request.IsActive is not null)
        {
            query = query.Where(i => i.IsActive == request.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(term) || (i.Code != null && i.Code.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(i => i.Name).ThenBy(i => i.Id)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<ActiveIngredientResponse>(items.Select(ToResponse).ToList(), request.Page, request.PageSize, total);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var ingredient = await FindAsync(id, cancellationToken);
        if (active)
        {
            ingredient.Activate();
        }
        else
        {
            ingredient.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var ingredient = await FindAsync(id, cancellationToken);
        if (await context.ProductActiveIngredients.AnyAsync(l => l.ActiveIngredientId == id, cancellationToken))
        {
            throw new ConflictException("Products use this active ingredient; remove it from them first or deactivate it.");
        }

        context.ActiveIngredients.Remove(ingredient);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<ActiveIngredient> FindAsync(Guid id, CancellationToken cancellationToken, bool tracking = true)
    {
        var query = tracking ? context.ActiveIngredients.AsQueryable() : context.ActiveIngredients.AsNoTracking();

        return await query.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new NotFoundException("Active ingredient", id);
    }

    private async Task EnsureNameIsFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lower = name.ToLower();
        if (await context.ActiveIngredients.AnyAsync(i => i.Id != exceptId && i.Name.ToLower() == lower, cancellationToken))
        {
            throw new ConflictException($"An active ingredient named '{name}' already exists.");
        }
    }

    private static ActiveIngredientResponse ToResponse(ActiveIngredient i) => new(i.Id, i.Code, i.Name, i.Description, i.IsActive);
}

public sealed class UnitService(IAgriSageDbContext context) : IUnitService
{
    public async Task<PagedResult<UnitResponse>> ListAsync(UnitListRequest request, CancellationToken cancellationToken)
    {
        var query = context.Units.AsNoTracking();
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(u => u.Code).Skip(request.Skip).Take(request.PageSize)
            .Select(u => new UnitResponse(u.Id, u.Code, u.Name, u.Symbol, u.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<UnitResponse>(items, request.Page, request.PageSize, total);
    }
}
