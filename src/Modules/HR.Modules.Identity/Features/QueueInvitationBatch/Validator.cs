using FluentValidation;

namespace HR.Modules.Identity.Features.QueueInvitationBatch;

internal sealed class QueueInvitationBatchValidator : AbstractValidator<QueueInvitationBatchRequest>
{
    public QueueInvitationBatchValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeIds).NotEmpty().WithMessage("At least one employee must be selected.");
    }
}
