using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.UpdateLeavePolicy;

internal sealed class Endpoint(
    UpdateLeavePolicyHandler handler) : Endpoint<UpdateLeavePolicyRequest, UpdateLeavePolicyResponse>
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
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
