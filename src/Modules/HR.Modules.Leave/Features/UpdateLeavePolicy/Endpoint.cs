using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.UpdateLeavePolicy;

internal sealed class Endpoint(
    UpdateLeavePolicyHandler handler, ICurrentUser currentUser) : Endpoint<UpdateLeavePolicyRequest, UpdateLeavePolicyResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/leave-policies/{policyId:guid}");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        UpdateLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
