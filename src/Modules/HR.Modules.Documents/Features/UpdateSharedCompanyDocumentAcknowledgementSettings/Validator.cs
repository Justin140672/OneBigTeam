using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed class UpdateSharedCompanyDocumentAcknowledgementSettingsValidator
    : AbstractValidator<UpdateSharedCompanyDocumentAcknowledgementSettingsRequest>
{
    public UpdateSharedCompanyDocumentAcknowledgementSettingsValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();

        RuleFor(r => r.AcknowledgementStatement)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.AcknowledgementStatement));
    }
}
