using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;

internal sealed class Endpoint(BulkApplyCompensationAdjustmentsHandler handler, ICurrentUser currentUser)
    : Endpoint<BulkApplyCompensationAdjustmentsRequest, BulkApplyCompensationAdjustmentsResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/compensation/bulk");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        BulkApplyCompensationAdjustmentsRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
