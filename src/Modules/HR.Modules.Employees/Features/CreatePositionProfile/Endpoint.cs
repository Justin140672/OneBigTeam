using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreatePositionProfile;

internal sealed class Endpoint(
    CreatePositionProfileHandler handler, ICurrentUser currentUser) : Endpoint<CreatePositionProfileRequest, CreatePositionProfileResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/position-profiles");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreatePositionProfileRequest request,
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
            $"/api/companies/{result.Value!.CompanyId}/position-profiles/{result.Value.Id}",
            result.Value));
    }
}
