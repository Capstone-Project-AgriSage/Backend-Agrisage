using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Diagnosis;

namespace AgriSage.Application.Features.Advisory;

// api-flows/ADVISORY.md: from a problem (typed or diagnosed) to products, quantity for the field and the cart.

public static class AdvisoryText
{
    public const string SafetyNote =
        "Đọc kỹ nhãn trước khi dùng, mang đồ bảo hộ, tuân thủ thời gian cách ly. Gợi ý chỉ mang tính tham khảo; hãy hỏi cửa hàng nếu chưa chắc.";

    public const string NoApprovedProduct = "NO_APPROVED_PRODUCT";

    public const string NothingInStore = "NOTHING_IN_STORE";
}

// Values of AdvisoryProduct.StockStatus: a status only, never a quantity (V7).
public static class AdvisoryStock
{
    public const string Enough = "ENOUGH";

    public const string NotEnough = "NOT_ENOUGH";

    public const string OutOfStock = "OUT_OF_STOCK";
}

// ---- Farmer (/api/me/advisory) ----

public sealed record AdvisoryProblemListRequest : PaginationRequest
{
    public string? Search { get; init; }

    public string? Type { get; init; }
}

public sealed record AdvisoryProblem(Guid Id, string Code, string Name, string ProblemType, string? Summary, string? MatchedBy);

public sealed record AdvisoryRecommendationRequest(
    Guid DiseaseId,
    decimal? AreaSqm = null,
    string? GrowthStage = null,
    Guid? DiagnosisCaseId = null,
    string? QueryText = null);

public sealed record AdvisoryProblemDetail(
    Guid Id, string Code, string Name, string ProblemType, string? Description, string? Symptoms, string? Causes, string? Prevention);

public sealed record AdvisoryPlanLine(Guid ProductPackagingId, string PackagingName, long Quantity, decimal UnitPrice, decimal LineAmount);

public sealed record AdvisoryPlan(long BaseQuantity, decimal TotalAmount, IReadOnlyList<AdvisoryPlanLine> Lines);

public sealed record AdvisoryProduct(
    Guid StoreProductId,
    Guid ProductId,
    string Name,
    string Sku,
    string? ImageUrl,
    string Group,
    int RankOrder,
    string Reason,
    string? RegistrationSource,
    string DoseText,
    int Applications,
    int? PreHarvestDays,
    IReadOnlyList<string> GrowthStages,
    string StockStatus,
    AdvisoryPlan? Plan);

public sealed record AdvisoryRecommendationResponse(
    Guid SessionId,
    AdvisoryProblemDetail Problem,
    IReadOnlyList<TreatmentResponse> CareGuidance,
    decimal? AreaSqm,
    string? GrowthStage,
    IReadOnlyList<AdvisoryProduct> Protection,
    IReadOnlyList<AdvisoryProduct> Nutrition,
    string? EmptyReason,
    string SafetyNote);

public interface IMyAdvisoryService
{
    Task<PagedResult<AdvisoryProblem>> ProblemsAsync(AdvisoryProblemListRequest request, CancellationToken cancellationToken);

    Task<AdvisoryRecommendationResponse> RecommendAsync(AdvisoryRecommendationRequest request, CancellationToken cancellationToken);
}

// ---- Staff: links between a problem and a product ----

public sealed record DiseaseProductRequest(
    string RecommendationGroup,
    string DoseText,
    string? RegistrationSource = null,
    string? Reason = null,
    string? DoseSource = null,
    decimal? DosePerHaBase = null,
    int Applications = 1,
    int? PreHarvestDays = null,
    IReadOnlyList<string>? GrowthStages = null,
    int Priority = 0);

public sealed record CreateDiseaseProductRequest(
    Guid ProductId,
    string RecommendationGroup,
    string DoseText,
    string? RegistrationSource = null,
    string? Reason = null,
    string? DoseSource = null,
    decimal? DosePerHaBase = null,
    int Applications = 1,
    int? PreHarvestDays = null,
    IReadOnlyList<string>? GrowthStages = null,
    int Priority = 0);

public sealed record DiseaseProductResponse(
    Guid Id,
    Guid DiseaseId,
    Guid ProductId,
    string Sku,
    string ProductName,
    string RecommendationGroup,
    string? RegistrationSource,
    string? Reason,
    string DoseText,
    string? DoseSource,
    decimal? DosePerHaBase,
    string BaseUnit,
    int Applications,
    int? PreHarvestDays,
    IReadOnlyList<string> GrowthStages,
    int Priority,
    string Status,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt);

public interface IDiseaseProductService
{
    Task<IReadOnlyList<DiseaseProductResponse>> ListAsync(Guid diseaseId, CancellationToken cancellationToken);

    Task<DiseaseProductResponse> CreateAsync(Guid diseaseId, CreateDiseaseProductRequest request, CancellationToken cancellationToken);

    Task<DiseaseProductResponse> UpdateAsync(Guid id, DiseaseProductRequest request, CancellationToken cancellationToken);

    Task<DiseaseProductResponse> ApproveAsync(Guid id, CancellationToken cancellationToken);

    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}

// ---- Staff: advisory sessions ----

public sealed record AdvisorySessionListRequest : PaginationRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public Guid? DiseaseId { get; init; }
}

public sealed record AdvisorySessionFarmer(Guid Id, string Name, string? Phone);

public sealed record AdvisorySessionProblem(Guid Id, string Name);

public sealed record AdvisorySessionItemResponse(Guid StoreProductId, string Name, string Group, int RankOrder);

public sealed record AdvisorySessionResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    AdvisorySessionFarmer Farmer,
    AdvisorySessionProblem Problem,
    string Source,
    decimal? AreaSqm,
    string? GrowthStage,
    IReadOnlyList<AdvisorySessionItemResponse> Items,
    bool BoughtWithin7Days);

public interface IAdvisorySessionService
{
    Task<PagedResult<AdvisorySessionResponse>> ListAsync(AdvisorySessionListRequest request, CancellationToken cancellationToken);
}
