using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Domain.Features.Products.Enums;

namespace AgriSage.Application.Features.Products;

// Pure catalog rules (no database): category tree and the conditions for selling a product.
public static class CatalogRules
{
    // True when moving the category under `newParentId` would make it its own ancestor (A → B → A).
    public static bool WouldCreateCycle(Guid categoryId, Guid? newParentId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var seen = new HashSet<Guid>();
        var current = newParentId;

        while (current is { } id)
        {
            if (id == categoryId || !seen.Add(id))
            {
                return true;
            }

            current = parents.TryGetValue(id, out var parent) ? parent : null;
        }

        return false;
    }

    // The category and all of its descendants.
    public static IReadOnlySet<Guid> SubtreeIds(Guid rootId, IEnumerable<(Guid Id, Guid? ParentId)> categories)
    {
        var childrenOf = categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value, c => c.Id);
        var result = new HashSet<Guid> { rootId };
        var pending = new Queue<Guid>([rootId]);

        while (pending.Count > 0)
        {
            foreach (var child in childrenOf[pending.Dequeue()])
            {
                if (result.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }

        return result;
    }

    // Roots first; each level ordered by display order then name. With activeOnly, an inactive category hides its subtree.
    public static IReadOnlyList<CategoryTreeNode> BuildTree(
        IReadOnlyCollection<CategoryResponse> categories,
        bool activeOnly)
    {
        var visible = activeOnly ? categories.Where(c => c.IsActive).ToList() : categories.ToList();
        var ids = visible.Select(c => c.Id).ToHashSet();
        var byParent = visible
            .Where(c => c.ParentId is not null && ids.Contains(c.ParentId.Value))
            .ToLookup(c => c.ParentId!.Value);

        IReadOnlyList<CategoryTreeNode> Build(IEnumerable<CategoryResponse> level) => level
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryTreeNode(c.Id, c.Code, c.Name, c.DisplayOrder, c.IsActive, Build(byParent[c.Id])))
            .ToList();

        // A category whose parent is missing (or hidden) is not a root: it is hidden with that parent.
        var roots = visible.Where(c => c.ParentId is null);
        return Build(roots);
    }

    // Why a product cannot be offered for sale in the store, or null when it can: the product must be ACTIVE and
    // have an ACTIVE base packaging and at least one ACTIVE sale packaging.
    public static string? SaleBlocker(ProductStatus status, IEnumerable<(bool IsBase, bool IsSale, string Status)> packagings)
    {
        if (status != ProductStatus.Active)
        {
            return "The product must be ACTIVE.";
        }

        var active = packagings.Where(p => p.Status == PackagingStatus.Active).ToList();

        if (!active.Any(p => p.IsBase))
        {
            return "The product has no ACTIVE base packaging.";
        }

        return active.Any(p => p.IsSale) ? null : "The product has no ACTIVE sale packaging.";
    }
}
