using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;

internal sealed class UpdateSharedCompanyDocumentAudienceValidator : AbstractValidator<UpdateSharedCompanyDocumentAudienceRequest>
{
    public UpdateSharedCompanyDocumentAudienceValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();
    }
}
