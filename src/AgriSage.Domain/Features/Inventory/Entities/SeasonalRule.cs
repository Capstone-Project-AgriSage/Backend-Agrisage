using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Inventory.Entities;

public sealed class SeasonalRule : SoftDeletableEntity, IHasConcurrencyVersion
{
    private readonly List<SeasonalRuleItem> _items = [];
    private SeasonalRule() { }
    public SeasonalRule(Guid storeId, string name, string cropType, string? region, DateOnly startsOn,
        DateOnly endsOn, int prepareDays, decimal demandFactor)
    {
        StoreId = storeId;
        Update(name, cropType, region, startsOn, endsOn, prepareDays, demandFactor, true);
    }
    public Guid StoreId { get; private set; }
    public string Name { get; private set; } = null!;
    public string CropType { get; private set; } = null!;
    public string? Region { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public int PrepareDays { get; private set; }
    public decimal DemandFactor { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<SeasonalRuleItem> Items => _items.AsReadOnly();
    public void Update(string name, string cropType, string? region, DateOnly startsOn, DateOnly endsOn,
        int prepareDays, decimal demandFactor, bool isActive)
    {
        if (endsOn < startsOn || prepareDays is < 0 or > 90 || demandFactor is < 0.1m or > 5m)
            throw new DomainException("Invalid season dates, preparation days or demand factor.");
        Name = Guard.NotNullOrWhiteSpace(name).Trim();
        CropType = Guard.NotNullOrWhiteSpace(cropType).Trim();
        Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        StartsOn = startsOn; EndsOn = endsOn; PrepareDays = prepareDays;
        DemandFactor = demandFactor; IsActive = isActive;
    }
    public void SetItems(IReadOnlyList<(Guid ProductId, long? Target)> lines, Guid actor, DateTimeOffset now)
    {
        if (lines.Count == 0 || lines.Select(l => l.ProductId).Distinct().Count() != lines.Count
            || lines.Any(l => l.ProductId == Guid.Empty || l.Target < 0))
            throw new DomainException("A seasonal rule requires distinct products and nonnegative targets.");
        foreach (var item in _items.Where(i => !i.IsDeleted && lines.All(l => l.ProductId != i.StoreProductId)))
            item.RemoveFromAggregate(actor, now);
        foreach (var line in lines)
        {
            var item = _items.SingleOrDefault(i => !i.IsDeleted && i.StoreProductId == line.ProductId);
            if (item is null) _items.Add(new SeasonalRuleItem(Id, line.ProductId, line.Target));
            else item.SetTarget(line.Target);
        }
    }
    public void Deactivate() => IsActive = false;
}

public sealed class SeasonalRuleItem : SoftDeletableChildEntity
{
    private SeasonalRuleItem() { }
    internal SeasonalRuleItem(Guid ruleId, Guid productId, long? target)
    { SeasonalRuleId = ruleId; StoreProductId = productId; SetTarget(target); }
    public Guid SeasonalRuleId { get; private set; }
    public Guid StoreProductId { get; private set; }
    public long? TargetStockLevelBase { get; private set; }
    internal void SetTarget(long? target)
    {
        if (target < 0) throw new DomainException("Seasonal stock target must be nonnegative.");
        TargetStockLevelBase = target;
    }
}
