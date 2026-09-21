using FluentValidation;

namespace HR.Modules.Identity.Features.RetryInvitationBatch;

internal sealed class RetryInvitationBatchValidator : AbstractValidator<RetryInvitationBatchRequest>
{
    public RetryInvitationBatchValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.BatchId).NotEmpty();
    }
}
