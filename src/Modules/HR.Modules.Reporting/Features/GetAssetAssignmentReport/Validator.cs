using FluentValidation;

namespace HR.Modules.Reporting.Features.GetAssetAssignmentReport;

internal sealed class GetAssetAssignmentReportValidator : AbstractValidator<GetAssetAssignmentReportRequest>
{
    public GetAssetAssignmentReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
