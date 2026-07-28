using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

// Ticket #99: the funnel stage set is now per-company/data-driven (RecruitmentStage rows), so this
// no longer restricts to a compiled list of "active pipeline" enum values — the handler itself scopes
// the query to the requested company, and a non-existent/foreign stage id simply yields zero rows.
internal sealed class GetApplicationsByStatusValidator : AbstractValidator<GetApplicationsByStatusRequest>
{
    public GetApplicationsByStatusValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.StageId).NotEmpty();
    }
}
