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

public sealed class CategoryService(IAgriSageDbContext context, IDatabaseErrorClassifier databaseErrors) : ICategoryService
{
    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        var lower = code.ToLower();
        if (await context.Categories.AnyAsync(c => c.Code.ToLower() == lower, cancellationToken))
        {
            throw new ConflictException($"A category with code '{code}' already exists.");
        }

        await EnsureParentExistsAsync(request.ParentId, cancellationToken);

        var siblings = await GetSiblingsAsync(request.ParentId, null, cancellationToken);
        var position = ClampPosition(request.DisplayOrder, siblings.Count);
        var category = new Category(code, request.Name.Trim(), request.ParentId, Texts.Clean(request.Description), position);
        context.Categories.Add(category);
        siblings.Insert(position, category);
        NormalizeSiblings(siblings);
        await SaveAsync(cancellationToken);

        return ToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await FindAsync(id, cancellationToken);
        var oldParentId = category.ParentId;
        await EnsureParentExistsAsync(request.ParentId, cancellationToken);

        if (request.ParentId is not null)
        {
            var parents = await context.Categories.AsNoTracking()
                .Select(c => new { c.Id, c.ParentId })
                .ToDictionaryAsync(c => c.Id, c => c.ParentId, cancellationToken);

            if (CatalogRules.WouldCreateCycle(id, request.ParentId, parents))
            {
                throw new BusinessRuleException("A category cannot be moved under itself or one of its sub-categories.");
            }
        }

        var oldSiblings = await GetSiblingsAsync(oldParentId, null, cancellationToken);
        var currentPosition = oldSiblings.FindIndex(item => item.Id == id);
        var newSiblings = oldParentId == request.ParentId
            ? oldSiblings.Where(item => item.Id != id).ToList()
            : await GetSiblingsAsync(request.ParentId, id, cancellationToken);
        var targetPosition = request.DisplayOrder is { } requested
            ? ClampPosition(requested, newSiblings.Count)
            : oldParentId == request.ParentId ? Math.Clamp(currentPosition < 0 ? newSiblings.Count : currentPosition, 0, newSiblings.Count) : newSiblings.Count;

        category.Update(request.Name.Trim(), Texts.Clean(request.Description), targetPosition);
        category.MoveTo(request.ParentId);
        if (oldParentId != request.ParentId)
        {
            NormalizeSiblings(oldSiblings.Where(item => item.Id != id).ToList());
        }
        newSiblings.Insert(targetPosition, category);
        NormalizeSiblings(newSiblings);
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(category);
    }

    public async Task<CategoryResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await FindAsync(id, cancellationToken, tracking: false));

    public async Task<PagedResult<CategoryResponse>> ListAsync(CategoryListRequest request, CancellationToken cancellationToken)
    {
        var query = context.Categories.AsNoTracking().AsQueryable();

        if (request.ParentId is not null)
        {
            query = query.Where(c => c.ParentId == request.ParentId);
        }

        if (request.IsActive is not null)
        {
            query = query.Where(c => c.IsActive == request.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || c.Code.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<CategoryResponse>(items.Select(ToResponse).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<IReadOnlyList<CategoryTreeNode>> GetTreeAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var all = await context.Categories.AsNoTracking().ToListAsync(cancellationToken);

        return CatalogRules.BuildTree(all.Select(ToResponse).ToList(), activeOnly);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var category = await FindAsync(id, cancellationToken);
        if (active)
        {
            category.Activate();
        }
        else
        {
            category.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderAsync(Guid id, int position, CancellationToken cancellationToken)
    {
        var category = await FindAsync(id, cancellationToken);
        var siblings = await GetSiblingsAsync(category.ParentId, null, cancellationToken);
        var remaining = siblings.Where(item => item.Id != id).ToList();
        var targetPosition = ClampPosition(position, remaining.Count);
        remaining.Insert(targetPosition, category);
        NormalizeSiblings(remaining);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await FindAsync(id, cancellationToken);

        if (await context.Categories.AnyAsync(c => c.ParentId == id, cancellationToken))
        {
            throw new ConflictException("The category has sub-categories; move or delete them first.");
        }

        if (await context.Products.AnyAsync(p => p.CategoryId == id, cancellationToken))
        {
            throw new ConflictException("The category has products; move them first or deactivate the category.");
        }

        var siblings = await GetSiblingsAsync(category.ParentId, id, cancellationToken);
        context.Categories.Remove(category);
        NormalizeSiblings(siblings);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<Category>> GetSiblingsAsync(Guid? parentId, Guid? excludedId, CancellationToken cancellationToken)
    {
        var query = context.Categories.Where(c => c.ParentId == parentId);
        if (excludedId is not null)
        {
            query = query.Where(c => c.Id != excludedId);
        }

        return await query.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ThenBy(c => c.Id).ToListAsync(cancellationToken);
    }

    private static int ClampPosition(int? requested, int count) => Math.Clamp(requested ?? count, 0, count);

    private static void NormalizeSiblings(IReadOnlyList<Category> siblings)
    {
        for (var index = 0; index < siblings.Count; index++)
        {
            var category = siblings[index];
            if (category.DisplayOrder != index)
            {
                category.Update(category.Name, category.Description, index);
            }
        }
    }

    private async Task<Category> FindAsync(Guid id, CancellationToken cancellationToken, bool tracking = true)
    {
        var query = tracking ? context.Categories.AsQueryable() : context.Categories.AsNoTracking();

        return await query.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Category", id);
    }

    private async Task EnsureParentExistsAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is not null && !await context.Categories.AnyAsync(c => c.Id == parentId, cancellationToken))
        {
            throw new NotFoundException("Parent category", parentId);
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
            throw new ConflictException("A category with the same unique value already exists.");
        }
    }

    internal static CategoryResponse ToResponse(Category c) =>
        new(c.Id, c.ParentId, c.Code, c.Name, c.Description, c.DisplayOrder, c.IsActive);
}
