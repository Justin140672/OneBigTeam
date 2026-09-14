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
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
