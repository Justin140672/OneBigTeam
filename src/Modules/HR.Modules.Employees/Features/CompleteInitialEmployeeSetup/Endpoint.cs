using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;

internal sealed class Endpoint(
    CompleteInitialEmployeeSetupHandler handler, ICurrentUser currentUser)
    : Endpoint<CompleteInitialEmployeeSetupRequest, CompleteInitialEmployeeSetupResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/me/complete-initial-setup");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        CompleteInitialEmployeeSetupRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(Route<string>("companyId"), out var companyId))
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            companyId, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
