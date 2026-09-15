using FastEndpoints;
using Microsoft.AspNetCore.Http;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.PublishVacancy;

internal sealed class Endpoint(PublishVacancyHandler handler)
    : Endpoint<PublishVacancyRequest, PublishVacancyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/publish");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        PublishVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
