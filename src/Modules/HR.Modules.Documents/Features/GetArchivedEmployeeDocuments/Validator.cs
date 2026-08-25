using FluentValidation;

namespace HR.Modules.Documents.Features.GetArchivedEmployeeDocuments;

internal sealed class GetArchivedEmployeeDocumentsValidator : AbstractValidator<GetArchivedEmployeeDocumentsRequest>
{
    public GetArchivedEmployeeDocumentsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
