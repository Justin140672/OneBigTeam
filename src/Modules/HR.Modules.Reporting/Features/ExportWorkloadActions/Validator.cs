using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportWorkloadActions;

internal sealed class ExportWorkloadActionsValidator : AbstractValidator<ExportWorkloadActionsRequest>
{
    private static readonly string[] AllowedGroupBy =
        ["ActionType", "AssignedUser", "Department", "DueDate"];

    public ExportWorkloadActionsValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();

        RuleFor(x => x.GroupBy)
            .Must(v => v is null || AllowedGroupBy.Contains(v))
            .WithMessage("GroupBy must be one of: ActionType, AssignedUser, Department, DueDate.");
    }
}
