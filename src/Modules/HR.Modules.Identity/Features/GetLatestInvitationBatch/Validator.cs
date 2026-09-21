using FluentValidation;

namespace HR.Modules.Identity.Features.GetLatestInvitationBatch;

internal sealed class GetLatestInvitationBatchValidator : AbstractValidator<GetLatestInvitationBatchRequest>
{
    public GetLatestInvitationBatchValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
