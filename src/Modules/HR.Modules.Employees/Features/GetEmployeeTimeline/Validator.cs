using FluentValidation;

namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed class GetEmployeeTimelineValidator : AbstractValidator<GetEmployeeTimelineRequest>
{
    public GetEmployeeTimelineValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 500);
    }
}
