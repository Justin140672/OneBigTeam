using FluentValidation;

namespace HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;

internal sealed class GetCompanyDocumentAcknowledgementReportValidator : AbstractValidator<GetCompanyDocumentAcknowledgementReportRequest>
{
    public GetCompanyDocumentAcknowledgementReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
