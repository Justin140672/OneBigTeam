using FluentValidation;

namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed class GetEmployeeTasksValidator : AbstractValidator<GetEmployeeTasksRequest>
{
    public GetEmployeeTasksValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 200);
    }
}
