using AgriSage.Application.Common.Models;
using FluentValidation;

namespace AgriSage.Application.Features.Inventory;

public sealed record SeasonalRuleListRequest : PaginationRequest
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}
public sealed record SeasonalRuleLine(Guid StoreProductId, long? TargetStockLevelBase = null);
public sealed record SeasonalRuleWriteRequest(string Name, string CropType, string? Region,
    DateOnly StartsOn, DateOnly EndsOn, int PrepareDays, decimal DemandFactor,
    IReadOnlyList<SeasonalRuleLine> Items, bool IsActive = true, long? Version = null);
public sealed record SeasonalRuleProduct(Guid StoreProductId, string Sku, string ProductName, long? TargetStockLevelBase);
public sealed record SeasonalRuleResponse(Guid Id, string Name, string CropType, string? Region,
    DateOnly StartsOn, DateOnly EndsOn, int PrepareDays, decimal DemandFactor, bool IsActive,
    long Version, IReadOnlyList<SeasonalRuleProduct> Items);
public sealed record SeasonalRuleSummary(long Total, long Active, long Upcoming, long Ended);
public sealed record SeasonalRulePage(IReadOnlyList<SeasonalRuleResponse> Items, int Page, int PageSize,
    long TotalCount, SeasonalRuleSummary Summary)
{
    public int TotalPages => (int)Math.Ceiling((decimal)TotalCount / PageSize);
}
public interface ISeasonalRuleService
{
    Task<SeasonalRulePage> ListAsync(SeasonalRuleListRequest request, CancellationToken token);
    Task<SeasonalRuleResponse> GetAsync(Guid id, CancellationToken token);
    Task<SeasonalRuleResponse> SaveAsync(Guid? id, SeasonalRuleWriteRequest request, CancellationToken token);
    Task<SeasonalRuleResponse> DeactivateAsync(Guid id, CancellationToken token);
}
public sealed class SeasonalRuleListRequestValidator : AbstractValidator<SeasonalRuleListRequest>
{
    public SeasonalRuleListRequestValidator()
    {
        Include(new AgriSage.Application.Common.Validators.PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r).Must(r => r.FromDate is null || r.ToDate is null || r.ToDate >= r.FromDate)
            .WithMessage("End date must not precede start date.");
    }
}
public sealed class SeasonalRuleWriteRequestValidator : AbstractValidator<SeasonalRuleWriteRequest>
{
    public SeasonalRuleWriteRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.CropType).NotEmpty().MaximumLength(50);
        RuleFor(r => r.Region).MaximumLength(150);
        RuleFor(r => r.StartsOn).GreaterThan(DateOnly.MinValue);
        RuleFor(r => r.EndsOn).GreaterThanOrEqualTo(r => r.StartsOn).LessThan(DateOnly.MaxValue);
        RuleFor(r => r.PrepareDays).InclusiveBetween(0, 90);
        RuleFor(r => r.DemandFactor).InclusiveBetween(0.1m, 5m).PrecisionScale(6, 3, true);
        RuleFor(r => r.Items).NotEmpty().Must(i => i is not null && i.Count <= 100);
        RuleForEach(r => r.Items).NotNull().ChildRules(i => {
            i.RuleFor(l => l.StoreProductId).NotEmpty();
            i.RuleFor(l => l.TargetStockLevelBase).Must(t => t is null || t >= 0);
        });
        RuleFor(r => r.Items).Must(i => i is null || i.Any(l => l is null)
            || i.Select(l => l.StoreProductId).Distinct().Count() == i.Count).WithMessage("Products must be distinct.");
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0).When(r => r.Version.HasValue);
    }
}
