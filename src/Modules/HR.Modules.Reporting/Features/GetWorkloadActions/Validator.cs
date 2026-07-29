using FluentValidation;

namespace HR.Modules.Reporting.Features.GetWorkloadActions;

internal sealed class GetWorkloadActionsValidator : AbstractValidator<GetWorkloadActionsRequest>
{
    private static readonly string[] AllowedGroupBy =
        ["ActionType", "AssignedUser", "Department", "DueDate"];

    public GetWorkloadActionsValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();

        RuleFor(x => x.GroupBy)
            .Must(v => v is null || AllowedGroupBy.Contains(v))
            .WithMessage("GroupBy must be one of: ActionType, AssignedUser, Department, DueDate.");
    }
}
