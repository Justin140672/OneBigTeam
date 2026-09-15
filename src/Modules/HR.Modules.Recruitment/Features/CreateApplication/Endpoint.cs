using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed class Endpoint(CreateApplicationHandler handler)
    : Endpoint<CreateApplicationRequest, CreateApplicationResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        CreateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/vacancies/{result.Value.VacancyId}/applications/{result.Value.Id}",
            result.Value));
    }
}
