using AgriSage.Application.Common.Validators;
using AgriSage.Application.Common;
using AgriSage.Domain.Features.Identity.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Audit;

public sealed class AuditLogListRequestValidator : AbstractValidator<AuditLogListRequest>
{
    public AuditLogListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Page).LessThanOrEqualTo(1_000_000);
        RuleFor(r => r.Action).MaximumLength(100);
        RuleFor(r => r.EntityType).MaximumLength(100);
        RuleFor(r => r.Search).MaximumLength(200);
        RuleFor(r => r.ActorRole).Must(value => string.IsNullOrWhiteSpace(value) || EnumText.TryParse<RoleCode>(value, out _))
            .WithMessage("ActorRole must be a supported role.");
        RuleFor(r => r.Status).Must(value => string.IsNullOrWhiteSpace(value)
            || value.Trim().Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("FAILURE", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Status must be SUCCESS or FAILURE.");
        RuleFor(r => r.To).Must((r, to) => r.From is null || to is null || to >= r.From)
            .WithMessage("To must be on or after From.");
    }
}
