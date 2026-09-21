using FluentValidation;

namespace HR.Modules.Identity.Features.GetInvitationBatchStatus;

internal sealed class GetInvitationBatchStatusValidator : AbstractValidator<GetInvitationBatchStatusRequest>
{
    public GetInvitationBatchStatusValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.BatchId).NotEmpty();
    }
}
