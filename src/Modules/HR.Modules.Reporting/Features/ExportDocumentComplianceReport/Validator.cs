using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportDocumentComplianceReport;

internal sealed class ExportDocumentComplianceReportValidator : AbstractValidator<ExportDocumentComplianceReportRequest>
{
    public ExportDocumentComplianceReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
