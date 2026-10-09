using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Notifications.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Notifications;

public sealed class NotificationListRequestValidator : AbstractValidator<NotificationListRequest>
{
    public NotificationListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Page).LessThanOrEqualTo(1_000_000);
        RuleFor(r => r.Status).Must(s => string.IsNullOrWhiteSpace(s) || EnumText.TryParse<NotificationStatus>(s, out _))
            .WithMessage("Invalid notification status.");
    }
}
