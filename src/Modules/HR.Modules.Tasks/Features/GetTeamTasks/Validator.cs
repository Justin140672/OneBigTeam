using FluentValidation;

namespace HR.Modules.Tasks.Features.GetTeamTasks;

internal sealed class GetTeamTasksValidator : AbstractValidator<GetTeamTasksRequest>
{
    public GetTeamTasksValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.ManagerId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 200);
    }
}
