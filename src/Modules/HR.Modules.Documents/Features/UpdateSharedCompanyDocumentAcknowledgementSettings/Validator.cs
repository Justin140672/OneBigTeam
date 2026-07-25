using FluentValidation;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed class UpdateSharedCompanyDocumentAcknowledgementSettingsValidator
    : AbstractValidator<UpdateSharedCompanyDocumentAcknowledgementSettingsRequest>
{
    public UpdateSharedCompanyDocumentAcknowledgementSettingsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();

        // Unlike UploadSharedCompanyDocumentValidator, the statement is NOT required here even when
        // RequiresAcknowledgement is true — EditSharedCompanyDocumentAcknowledgementDialog.razor
        // documents it as optional, falling back to a default placeholder shown to employees, and
        // the handler already normalizes a blank statement to null rather than rejecting it.
        RuleFor(r => r.AcknowledgementStatement)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.AcknowledgementStatement));
    }
}
