using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetInternalVacancy;

internal sealed class Endpoint(GetInternalVacancyHandler handler)
    : Endpoint<GetInternalVacancyRequest, GetInternalVacancyResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/internal-vacancies/{vacancyId:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetInternalVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
