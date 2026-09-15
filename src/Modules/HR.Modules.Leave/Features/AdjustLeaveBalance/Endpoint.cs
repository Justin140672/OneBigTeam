using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.AdjustLeaveBalance;

internal sealed class Endpoint(AdjustLeaveBalanceHandler handler, ICurrentUser currentUser) : Endpoint<AdjustLeaveBalanceRequest, AdjustLeaveBalanceResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-balance-adjustments");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(AdjustLeaveBalanceRequest request, CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } adjustedByEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with
            {
                AdjustedByEmployeeId = adjustedByEmployeeId,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            },
            cancellationToken);

        if (result.IsFailure)
        {
            // P1 #4: routes "concurrency" (as well as "conflict") to 409, matching every other
            // versioned-aggregate endpoint (see ProblemResults.FromError).
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
