using FluentValidation;

namespace HR.Modules.Companies.Features.RetryBackgroundJob;

internal sealed class RetryBackgroundJobValidator : AbstractValidator<RetryBackgroundJobRequest>
{
    public RetryBackgroundJobValidator()
    {
        RuleFor(r => r.JobId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
