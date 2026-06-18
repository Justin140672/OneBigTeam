using FluentValidation;

namespace HR.Modules.Documents.Features.GetEmployeeDocument;

internal sealed class GetEmployeeDocumentValidator : AbstractValidator<GetEmployeeDocumentRequest>
{
    public GetEmployeeDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.EmployeeDocumentId).NotEmpty();
    }
}
