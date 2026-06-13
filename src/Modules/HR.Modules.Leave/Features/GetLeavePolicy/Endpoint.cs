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
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
