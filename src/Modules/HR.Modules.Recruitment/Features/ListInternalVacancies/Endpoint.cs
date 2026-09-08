using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListInternalVacancies;

internal sealed class Endpoint(ListInternalVacanciesHandler handler)
    : Endpoint<ListInternalVacanciesRequest, ListInternalVacanciesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/internal-vacancies");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListInternalVacanciesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
