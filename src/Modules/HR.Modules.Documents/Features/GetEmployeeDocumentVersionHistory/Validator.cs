using FluentValidation;

namespace HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;

internal sealed class GetEmployeeDocumentVersionHistoryValidator : AbstractValidator<GetEmployeeDocumentVersionHistoryRequest>
{
    public GetEmployeeDocumentVersionHistoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.EmployeeDocumentId).NotEmpty();
    }
}
