using FluentValidation;

namespace HR.Modules.Companies.Features.GetFailedPayments;

internal sealed class GetFailedPaymentsValidator : AbstractValidator<GetFailedPaymentsRequest>
{
    private static readonly string[] AllowedStatuses = ["open", "uncollectible"];

    public GetFailedPaymentsValidator()
    {
        RuleFor(r => r.Search)
            .MaximumLength(200)
            .When(r => r.Search is not null);

        RuleFor(r => r.StatusFilter)
            .Must(status => AllowedStatuses.Contains(status))
            .WithMessage("StatusFilter must be 'open' or 'uncollectible'.")
            .When(r => r.StatusFilter is not null);
    }
}
