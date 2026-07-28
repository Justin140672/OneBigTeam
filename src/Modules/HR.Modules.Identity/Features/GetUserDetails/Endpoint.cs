using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetUserDetails;

internal sealed class Endpoint(GetUserDetailsHandler handler) : Endpoint<GetUserDetailsRequest, GetUserDetailsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/{employeeId:guid}");
        Policies("users:view");
    }

    public override async Task HandleAsync(GetUserDetailsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
