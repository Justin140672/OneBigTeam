using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ApproveOffer;

internal sealed class Endpoint(ApproveOfferHandler handler, ICurrentUser currentUser)
    : Endpoint<ApproveOfferRequest, ApproveOfferResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/approve-offer");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        ApproveOfferRequest request,
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
