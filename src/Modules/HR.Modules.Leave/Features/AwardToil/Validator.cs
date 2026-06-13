using FluentValidation;

namespace HR.Modules.Leave.Features.AwardToil;

internal sealed class AwardToilValidator : AbstractValidator<AwardToilRequest>
{
    public AwardToilValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.AwardedByEmployeeId).NotEmpty();
        RuleFor(r => r.Days).GreaterThan(0);
        RuleFor(r => r.OccurredOn).NotEmpty();
        RuleFor(r => r.Notes).MaximumLength(500).When(r => r.Notes is not null);
    }
}
