using FluentValidation;

namespace HR.Modules.Documents.Features.PurgeEligibleArchivedEmployeeDocuments;

internal sealed class PurgeEligibleArchivedEmployeeDocumentsValidator
    : AbstractValidator<PurgeEligibleArchivedEmployeeDocumentsRequest>
{
    public PurgeEligibleArchivedEmployeeDocumentsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
