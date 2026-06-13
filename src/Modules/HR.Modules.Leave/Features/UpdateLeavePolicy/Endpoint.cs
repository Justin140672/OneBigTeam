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
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await SendResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
