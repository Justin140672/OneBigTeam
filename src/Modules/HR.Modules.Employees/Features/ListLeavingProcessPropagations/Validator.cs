using FluentValidation;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.ListLeavingProcessPropagations;

internal sealed class ListLeavingProcessPropagationsValidator : AbstractValidator<ListLeavingProcessPropagationsRequest>
{
    public ListLeavingProcessPropagationsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Limit).InclusiveBetween(1, 500);
        RuleFor(r => r.Status)
            .Must(s => s is LeavingProcessPropagation.StatusPending
                or LeavingProcessPropagation.StatusProcessed
                or LeavingProcessPropagation.StatusFailed)
            .When(r => r.Status is not null)
            .WithMessage("Status must be one of: pending, processed, failed.");
    }
}
