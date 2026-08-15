using FluentValidation;

namespace HR.Modules.Companies.Features.ScheduleCustomerDeletion;

internal sealed class ScheduleCustomerDeletionValidator : AbstractValidator<ScheduleCustomerDeletionRequest>
{
    public ScheduleCustomerDeletionValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);

        RuleFor(r => r.CountdownDays)
            .InclusiveBetween(1, 365)
            .When(r => r.CountdownDays is not null);
    }
}
