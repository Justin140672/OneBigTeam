using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ApproveVacancy;

internal sealed class Endpoint(ApproveVacancyHandler handler, ICurrentUser currentUser)
    : Endpoint<ApproveVacancyRequest, ApproveVacancyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/approve");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        ApproveVacancyRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid approvedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            approvedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
