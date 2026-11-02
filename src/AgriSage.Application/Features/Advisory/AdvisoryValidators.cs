using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Advisory;

public sealed class AdvisoryProblemListRequestValidator : AbstractValidator<AdvisoryProblemListRequest>
{
    public AdvisoryProblemListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.Type).Must(type => EnumText.TryParse<ProblemType>(type, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.Type))
            .WithMessage("Type must be DISEASE, PEST, WEED, NUTRITION or GROWTH.");
    }
}

public sealed class RecommendationRequestValidator : AbstractValidator<AdvisoryRecommendationRequest>
{
    public RecommendationRequestValidator()
    {
        RuleFor(r => r.DiseaseId).NotEmpty();
        RuleFor(r => r.AreaSqm).GreaterThan(0).LessThanOrEqualTo(AdvisorySession.MaxAreaSqm).When(r => r.AreaSqm is not null)
            .WithMessage($"The area must be greater than zero and at most {AdvisorySession.MaxAreaSqm:0} square metres.");
        RuleFor(r => r.GrowthStage).Must(stage => EnumText.TryParse<GrowthStage>(stage, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.GrowthStage))
            .WithMessage("GrowthStage must be SEEDLING, TILLERING, BOOTING, HEADING or RIPENING.");
        RuleFor(r => r.QueryText).MaximumLength(200);
    }
}

public sealed class CreateDiseaseProductRequestValidator : AbstractValidator<CreateDiseaseProductRequest>
{
    public CreateDiseaseProductRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.RecommendationGroup).NotEmpty().Must(group => EnumText.TryParse<RecommendationGroup>(group, out _))
            .WithMessage("RecommendationGroup must be PROTECTION or NUTRITION.");
        RuleFor(r => r.DoseText).NotEmpty().MaximumLength(500);
        RuleFor(r => r.RegistrationSource).MaximumLength(200);
        RuleFor(r => r.Reason).MaximumLength(300);
        RuleFor(r => r.DoseSource).MaximumLength(200);
        RuleFor(r => r.DosePerHaBase).GreaterThan(0).PrecisionScale(12, 3, ignoreTrailingZeros: true).When(r => r.DosePerHaBase is not null);
        RuleFor(r => r.Applications).InclusiveBetween(1, DiseaseProduct.MaxApplications);
        RuleFor(r => r.PreHarvestDays).GreaterThanOrEqualTo(0).When(r => r.PreHarvestDays is not null);
        RuleForEach(r => r.GrowthStages).Must(stage => EnumText.TryParse<GrowthStage>(stage, out _))
            .WithMessage("GrowthStages must be SEEDLING, TILLERING, BOOTING, HEADING or RIPENING.");
    }
}

public sealed class DiseaseProductRequestValidator : AbstractValidator<DiseaseProductRequest>
{
    public DiseaseProductRequestValidator()
    {
        RuleFor(r => r.RecommendationGroup).NotEmpty().Must(group => EnumText.TryParse<RecommendationGroup>(group, out _))
            .WithMessage("RecommendationGroup must be PROTECTION or NUTRITION.");
        RuleFor(r => r.DoseText).NotEmpty().MaximumLength(500);
        RuleFor(r => r.RegistrationSource).MaximumLength(200);
        RuleFor(r => r.Reason).MaximumLength(300);
        RuleFor(r => r.DoseSource).MaximumLength(200);
        RuleFor(r => r.DosePerHaBase).GreaterThan(0).PrecisionScale(12, 3, ignoreTrailingZeros: true).When(r => r.DosePerHaBase is not null);
        RuleFor(r => r.Applications).InclusiveBetween(1, DiseaseProduct.MaxApplications);
        RuleFor(r => r.PreHarvestDays).GreaterThanOrEqualTo(0).When(r => r.PreHarvestDays is not null);
        RuleForEach(r => r.GrowthStages).Must(stage => EnumText.TryParse<GrowthStage>(stage, out _))
            .WithMessage("GrowthStages must be SEEDLING, TILLERING, BOOTING, HEADING or RIPENING.");
    }
}

public sealed class AdvisorySessionListRequestValidator : AbstractValidator<AdvisorySessionListRequest>
{
    public AdvisorySessionListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.To).GreaterThanOrEqualTo(r => r.From!.Value).When(r => r.From is not null && r.To is not null)
            .WithMessage("'to' must not be before 'from'.");
    }
}
