using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class Endpoint(CreateVacancyHandler handler)
    : Endpoint<CreateVacancyRequest, CreateVacancyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        CreateVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/vacancies/{result.Value.Id}",
            result.Value));
    }
}
