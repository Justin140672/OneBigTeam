using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.PurgeEligibleCandidates;

internal sealed class Endpoint(PurgeEligibleCandidatesHandler handler, ICurrentUser currentUser)
    : Endpoint<PurgeEligibleCandidatesRequest, PurgeEligibleCandidatesResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/candidates/purge-eligible");
        Policies("role:company-administrator");
    }

    public override async Task HandleAsync(
        PurgeEligibleCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid purgedBy)
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
            purgedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };
            await Send.ResultAsync(result.Error.Code == "conflict"
                ? TypedResults.Conflict(businessError)
                : TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
