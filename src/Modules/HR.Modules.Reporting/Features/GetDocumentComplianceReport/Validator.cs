using FluentValidation;

namespace HR.Modules.Reporting.Features.GetDocumentComplianceReport;

internal sealed class GetDocumentComplianceReportValidator : AbstractValidator<GetDocumentComplianceReportRequest>
{
    public GetDocumentComplianceReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
