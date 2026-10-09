using AgriSage.Application.Common.Validators;
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
        RuleFor(r => r.To).Must((r, to) => r.From is null || to is null || to >= r.From)
            .WithMessage("To must be on or after From.");
    }
}
