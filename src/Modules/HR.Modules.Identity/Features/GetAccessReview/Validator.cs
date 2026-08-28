using FluentValidation;

namespace HR.Modules.Identity.Features.GetAccessReview;

internal sealed class GetAccessReviewValidator : AbstractValidator<GetAccessReviewRequest>
{
    public GetAccessReviewValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
