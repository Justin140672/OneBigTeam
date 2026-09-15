using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

internal sealed class Endpoint(AddRequiredDocumentHandler handler, ICurrentUser currentUser)
    : Endpoint<AddRequiredDocumentRequest, AddRequiredDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/required-documents");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        AddRequiredDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/position-profiles/{request.PositionProfileId}/required-documents/{result.Value!.Id}",
            result.Value));
    }
}
