using FluentValidation;

namespace HR.Modules.Documents.Features.ListEmployeeDocuments;

internal sealed class ListEmployeeDocumentsValidator : AbstractValidator<ListEmployeeDocumentsRequest>
{
    public ListEmployeeDocumentsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
