using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Documents.Features.UpdateDocumentType;

internal sealed class UpdateDocumentTypeValidator : AbstractValidator<UpdateDocumentTypeRequest>
{
    public UpdateDocumentTypeValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.DocumentTypeId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(1000)
            .When(r => !string.IsNullOrWhiteSpace(r.Description));
    }
}
