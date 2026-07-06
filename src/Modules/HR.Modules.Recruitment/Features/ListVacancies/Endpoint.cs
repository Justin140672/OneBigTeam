using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed class Endpoint(ListVacanciesHandler handler)
    : Endpoint<ListVacanciesRequest, ListVacanciesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        ListVacanciesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
