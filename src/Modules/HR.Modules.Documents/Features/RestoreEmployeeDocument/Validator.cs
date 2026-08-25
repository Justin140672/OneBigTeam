using FluentValidation;

namespace HR.Modules.Documents.Features.RestoreEmployeeDocument;

internal sealed class RestoreEmployeeDocumentValidator : AbstractValidator<RestoreEmployeeDocumentRequest>
{
    public RestoreEmployeeDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.EmployeeDocumentId).NotEmpty();
    }
}
