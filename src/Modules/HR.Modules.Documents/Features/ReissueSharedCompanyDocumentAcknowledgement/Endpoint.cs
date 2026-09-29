using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ReissueSharedCompanyDocumentAcknowledgement;

internal sealed class Endpoint(ReissueSharedCompanyDocumentAcknowledgementHandler handler, ICurrentUser currentUser)
    : Endpoint<ReissueSharedCompanyDocumentAcknowledgementRequest, ReissueSharedCompanyDocumentAcknowledgementResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/reissue-acknowledgement");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        ReissueSharedCompanyDocumentAcknowledgementRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid reissuedBy)
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
            reissuedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.OkAsync(result.Value!, cancellation: cancellationToken);
    }
}
