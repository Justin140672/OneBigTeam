using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed class Endpoint(ListApplicationsForVacancyHandler handler)
    : Endpoint<ListApplicationsForVacancyRequest, ListApplicationsForVacancyResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListApplicationsForVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
