using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetVacancy;

internal sealed class Endpoint(GetVacancyHandler handler)
    : Endpoint<GetVacancyRequest, GetVacancyResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        GetVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
