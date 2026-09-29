using AgriSage.Application.Common.Models;
using FluentValidation;

namespace AgriSage.Application.Common.Validators;

// Feature list-request validators reuse this via Include(new PaginationRequestValidator()).
public sealed class PaginationRequestValidator : AbstractValidator<PaginationRequest>
{
    public PaginationRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PaginationRequest.MaxPageSize);
    }
}
