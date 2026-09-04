using FluentValidation;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;

namespace HR.Modules.Tasks.Features.GetTeamTasks;

internal sealed class GetTeamTasksValidator : AbstractValidator<GetTeamTasksRequest>
{
    public GetTeamTasksValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.ManagerId)
            .NotEmpty();

        RuleFor(r => r.Status)
            .Must(TaskListFilterValidation.IsValidOptionalFilter<TaskItemStatus>)
            .WithMessage(TaskListFilterValidation.AllowedValuesMessage<TaskItemStatus>("Status"));

        RuleFor(r => r.Priority)
            .Must(TaskListFilterValidation.IsValidOptionalFilter<TaskPriority>)
            .WithMessage(TaskListFilterValidation.AllowedValuesMessage<TaskPriority>("Priority"));

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 200);
    }
}
