using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetInterviewsTodayCount;

internal sealed class GetInterviewsTodayCountValidator : AbstractValidator<GetInterviewsTodayCountRequest>
{
    public GetInterviewsTodayCountValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
