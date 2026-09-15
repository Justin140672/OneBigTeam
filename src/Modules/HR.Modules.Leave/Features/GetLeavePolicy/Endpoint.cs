using FastEndpoints;
using HR.SharedKernel;
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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
