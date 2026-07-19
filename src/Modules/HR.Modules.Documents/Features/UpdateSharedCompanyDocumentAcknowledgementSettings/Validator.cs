using FluentValidation;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed class UpdateSharedCompanyDocumentAcknowledgementSettingsValidator
    : AbstractValidator<UpdateSharedCompanyDocumentAcknowledgementSettingsRequest>
{
    public UpdateSharedCompanyDocumentAcknowledgementSettingsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();

        RuleFor(r => r.AcknowledgementStatement)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.AcknowledgementStatement));

        RuleFor(r => r.AcknowledgementStatement)
            .NotEmpty()
            .WithMessage("An acknowledgement statement is required when acknowledgement is required.")
            .When(r => r.RequiresAcknowledgement);
    }
}
