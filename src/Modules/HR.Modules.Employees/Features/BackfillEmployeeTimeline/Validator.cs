using FluentValidation;

namespace HR.Modules.Employees.Features.BackfillEmployeeTimeline;

internal sealed class BackfillEmployeeTimelineValidator : AbstractValidator<BackfillEmployeeTimelineRequest>
{
    public BackfillEmployeeTimelineValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
