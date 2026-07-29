using FluentValidation;

namespace HR.Modules.Reporting.Features.GetSicknessReport;

internal sealed class GetSicknessReportValidator : AbstractValidator<GetSicknessReportRequest>
{
    public GetSicknessReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.GroupBy).IsInEnum();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate is not null && x.EndDate is not null);
    }
}
