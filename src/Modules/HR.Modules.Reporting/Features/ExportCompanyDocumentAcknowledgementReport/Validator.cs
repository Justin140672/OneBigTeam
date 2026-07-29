using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportCompanyDocumentAcknowledgementReport;

internal sealed class ExportCompanyDocumentAcknowledgementReportValidator : AbstractValidator<ExportCompanyDocumentAcknowledgementReportRequest>
{
    public ExportCompanyDocumentAcknowledgementReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
