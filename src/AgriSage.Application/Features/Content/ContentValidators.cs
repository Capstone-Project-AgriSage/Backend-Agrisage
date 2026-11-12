using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Content.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Content;

public sealed class ArticleRequestValidator : AbstractValidator<ArticleRequest>
{
    public ArticleRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Slug).NotEmpty().MaximumLength(255).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$");
        RuleFor(r => r.Content).NotEmpty().MaximumLength(100000);
        RuleFor(r => r.Summary).MaximumLength(1000);
        RuleFor(r => r.ThumbnailUrl).NotEmpty().WithMessage("Bài viết phải có ảnh đại diện.")
            .MaximumLength(1000).Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
    }
}
public sealed class ArticleStatusRequestValidator : AbstractValidator<ArticleStatusRequest>
{
    public ArticleStatusRequestValidator() => RuleFor(r => r.Status).Must(s => EnumText.TryParse<ArticleStatus>(s, out _));
}
public sealed class ArticleListRequestValidator : AbstractValidator<ArticleListRequest>
{
    public ArticleListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.Status).Must(s => s is null || EnumText.TryParse<ArticleStatus>(s, out _));
    }
}
public sealed class ContactRequestCreateValidator : AbstractValidator<ContactRequestCreate>
{
    public ContactRequestCreateValidator()
    {
        RuleFor(r => r.ContactName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Subject).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Message).NotEmpty().MaximumLength(10000);
        RuleFor(r => r.ContactEmail).MaximumLength(255).EmailAddress().When(r => !string.IsNullOrWhiteSpace(r.ContactEmail));
        RuleFor(r => r.ContactPhone).MaximumLength(20).Matches(@"^\+?[0-9][0-9\s.-]{7,19}$")
            .When(r => !string.IsNullOrWhiteSpace(r.ContactPhone));
        RuleFor(r => r.ContactEmail).NotEmpty().When(r => string.IsNullOrWhiteSpace(r.ContactPhone));
    }
}
public sealed class ContactRequestUpdateValidator : AbstractValidator<ContactRequestUpdate>
{
    public ContactRequestUpdateValidator()
    {
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<ContactRequestStatus>(s, out _));
        RuleFor(r => r.ResolutionNote).MaximumLength(2000).NotEmpty()
            .When(r => EnumText.TryParse<ContactRequestStatus>(r.Status, out var status) && status == ContactRequestStatus.Resolved, ApplyConditionTo.CurrentValidator);
    }
}
public sealed class ContactRequestListValidator : AbstractValidator<ContactRequestList>
{
    public ContactRequestListValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.Status).Must(s => s is null || EnumText.TryParse<ContactRequestStatus>(s, out _));
    }
}
