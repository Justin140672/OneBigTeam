using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetLeavePolicy;

internal sealed class Endpoint(
    GetLeavePolicyHandler handler) : Endpoint<GetLeavePolicyRequest, GetLeavePolicyResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/leave-policies/{id:guid}");
        Policies("leave:approve");
    }

    public override async Task HandleAsync(
        GetLeavePolicyRequest request,
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

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
