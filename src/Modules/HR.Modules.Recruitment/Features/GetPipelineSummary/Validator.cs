using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetPipelineSummary;

internal sealed class GetPipelineSummaryValidator : AbstractValidator<GetPipelineSummaryRequest>
{
    public GetPipelineSummaryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
