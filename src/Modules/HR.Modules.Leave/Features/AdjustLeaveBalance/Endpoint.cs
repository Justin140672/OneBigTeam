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

        var result = await handler.HandleAsync(
            request with { AdjustedByEmployeeId = adjustedByEmployeeId },
            cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
