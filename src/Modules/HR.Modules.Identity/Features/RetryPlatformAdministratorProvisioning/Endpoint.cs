using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.RetryPlatformAdministratorProvisioning;

internal sealed class Endpoint(
    RetryPlatformAdministratorProvisioningHandler handler,
    ICurrentUser currentUser) : Endpoint<RetryPlatformAdministratorProvisioningRequest, RetryPlatformAdministratorProvisioningResponse>
{
    public override void Configure()
    {
        Post("/api/platform-administrators/{Id}/retry-provisioning");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(RetryPlatformAdministratorProvisioningRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, currentUser, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
