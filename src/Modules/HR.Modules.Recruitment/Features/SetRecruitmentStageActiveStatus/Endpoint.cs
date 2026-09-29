using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.SetRecruitmentStageActiveStatus;

internal sealed class Endpoint(SetRecruitmentStageActiveStatusHandler handler, ICurrentUser currentUser)
    : Endpoint<SetRecruitmentStageActiveStatusRequest, SetRecruitmentStageActiveStatusResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/recruitment-stages/{recruitmentStageId:guid}/active-status");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        SetRecruitmentStageActiveStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

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

            await Send.ResultAsync(TypedResults.UnprocessableEntity(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
