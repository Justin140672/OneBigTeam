using FluentValidation;

namespace HR.Modules.Identity.Features.ExportAccessReview;

internal sealed class ExportAccessReviewValidator : AbstractValidator<ExportAccessReviewRequest>
{
    public ExportAccessReviewValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
