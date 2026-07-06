using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListInterviewsForVacancy;

internal sealed class Endpoint(ListInterviewsForVacancyHandler handler)
    : Endpoint<ListInterviewsForVacancyRequest, ListInterviewsForVacancyResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/interviews");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        ListInterviewsForVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
