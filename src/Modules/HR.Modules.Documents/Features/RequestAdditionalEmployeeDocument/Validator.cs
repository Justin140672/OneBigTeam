using FastEndpoints;
using FluentValidation;

namespace HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;

internal sealed class Validator : Validator<RequestAdditionalEmployeeDocumentRequest>
{
    public Validator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.DocumentTypeId).NotEmpty();
    }
}
