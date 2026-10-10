using System.Text.Json;
using AgriSage.Application.Common.Validators;
using FluentValidation;

namespace AgriSage.Application.Features.Diagnosis;

public sealed class MyDiagnosisCaseListRequestValidator : AbstractValidator<MyDiagnosisCaseListRequest>
{
    public MyDiagnosisCaseListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).MaximumLength(30);
    }
}

public sealed class CancelDiagnosisRequestValidator : AbstractValidator<CancelDiagnosisRequest>
{
    public CancelDiagnosisRequestValidator() => RuleFor(r => r.Reason).MaximumLength(500);
}

public sealed class DiagnosisCaseListRequestValidator : AbstractValidator<DiagnosisCaseListRequest>
{
    public DiagnosisCaseListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).MaximumLength(30);
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.To).GreaterThanOrEqualTo(r => r.From!.Value).When(r => r.From is not null && r.To is not null)
            .WithMessage("'to' must not be before 'from'.");
    }
}

public sealed class ReviewRequestValidator : AbstractValidator<ReviewRequest>
{
    public ReviewRequestValidator()
    {
        RuleFor(r => r.Decision).NotEmpty().MaximumLength(30);
        RuleFor(r => r.Comment).MaximumLength(2000);
    }
}

public sealed class RecommendationRequestValidator : AbstractValidator<RecommendationRequest>
{
    public RecommendationRequestValidator()
    {
        RuleFor(r => r.Type).NotEmpty().MaximumLength(30);
        RuleFor(r => r.RankOrder).InclusiveBetween(0, 1000);
        RuleFor(r => r.Reason).MaximumLength(1000);
    }
}

public sealed class AiModelListRequestValidator : AbstractValidator<AiModelListRequest>
{
    public AiModelListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).MaximumLength(30);
    }
}

public sealed class AiModelRequestValidator : AbstractValidator<AiModelRequest>
{
    public AiModelRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Version).NotEmpty().MaximumLength(50);
        RuleFor(r => r.Framework).NotEmpty().MaximumLength(50);
        RuleFor(r => r.Architecture).MaximumLength(100);
        RuleFor(r => r.ModelStorageUrl).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.InputWidth).GreaterThan(0).When(r => r.InputWidth is not null);
        RuleFor(r => r.InputHeight).GreaterThan(0).When(r => r.InputHeight is not null);
        RuleFor(r => r.ClassLabels).NotNull();
        RuleForEach(r => r.ClassLabels).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Metrics).Must(IsObjectOrAbsent).WithMessage("Metrics must be a JSON object.");
    }

    private static bool IsObjectOrAbsent(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined;
}

public sealed class AiPolicyRequestValidator : AbstractValidator<AiPolicyRequest>
{
    public AiPolicyRequestValidator()
    {
        RuleFor(r => r.Version).NotEmpty().MaximumLength(50);
        RuleFor(r => r.MinimumConfidence).InclusiveBetween(0m, 1m).PrecisionScale(5, 4, false);
        RuleFor(r => r.MinimumMargin).InclusiveBetween(0m, 1m).PrecisionScale(5, 4, false).When(r => r.MinimumMargin is not null);
        RuleFor(r => r.TopK).InclusiveBetween(1, 10);
        RuleFor(r => r.EffectiveTo).GreaterThan(r => r.EffectiveFrom).When(r => r.EffectiveTo is not null)
            .WithMessage("effectiveTo must be after effectiveFrom.");
        RuleFor(r => r.Parameters).Must(IsObjectOrAbsent).WithMessage("Parameters must be a JSON object.");
    }

    private static bool IsObjectOrAbsent(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined;
}

public sealed class DiseaseUpdateRequestValidator : AbstractValidator<DiseaseUpdateRequest>
{
    public DiseaseUpdateRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.ScientificName).MaximumLength(255);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.Symptoms).MaximumLength(4000);
        RuleFor(r => r.Causes).MaximumLength(4000);
        RuleFor(r => r.Prevention).MaximumLength(4000);
    }
}

public sealed class TreatmentRequestValidator : AbstractValidator<TreatmentRequest>
{
    public TreatmentRequestValidator()
    {
        RuleFor(r => r.TreatmentType).NotEmpty().MaximumLength(30);
        RuleFor(r => r.Title).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Instructions).NotEmpty().MaximumLength(4000);
        RuleFor(r => r.Precautions).MaximumLength(2000);
        RuleFor(r => r.Priority).InclusiveBetween(0, 1000);
    }
}

public sealed class SetAiReviewRequestValidator : AbstractValidator<SetAiReviewRequest>
{
    public SetAiReviewRequestValidator() => RuleFor(r => r.Reason).MaximumLength(500);
}
