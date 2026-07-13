using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetStaleVacancies;

internal sealed class Endpoint(GetStaleVacanciesHandler handler)
    : Endpoint<GetStaleVacanciesRequest, GetStaleVacanciesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/stale");
        // Matches CreateVacancy/ScheduleInterview's policy — actionable recruitment-pipeline
        // insight for Recruiter + HrAdministrator only.
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        GetStaleVacanciesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
