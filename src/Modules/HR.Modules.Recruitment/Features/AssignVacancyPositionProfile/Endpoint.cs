using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.AssignVacancyPositionProfile;

internal sealed class Endpoint(AssignVacancyPositionProfileHandler handler)
    : Endpoint<AssignVacancyPositionProfileRequest, AssignVacancyPositionProfileResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/position-profile");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        AssignVacancyPositionProfileRequest request,
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
