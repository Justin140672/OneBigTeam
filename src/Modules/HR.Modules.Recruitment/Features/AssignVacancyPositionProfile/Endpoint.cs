using FastEndpoints;
using HR.SharedKernel;
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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
