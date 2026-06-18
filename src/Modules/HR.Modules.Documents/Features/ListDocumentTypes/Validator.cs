using FluentValidation;

namespace HR.Modules.Documents.Features.ListDocumentTypes;

internal sealed class ListDocumentTypesValidator : AbstractValidator<ListDocumentTypesRequest>
{
    public ListDocumentTypesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
