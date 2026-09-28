using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.SetCustomerOriginalStatus;

internal sealed class Endpoint(
    SetCustomerOriginalStatusHandler handler) : Endpoint<SetCustomerOriginalStatusRequest, SetCustomerOriginalStatusResponse>
{
    public override void Configure()
    {
        Patch("/api/platform-admin/companies/{companyId:guid}/original-customer-status");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(SetCustomerOriginalStatusRequest req, CancellationToken cancellationToken)
    {
        var companyId = Route<Guid>("companyId");

        var result = await handler.HandleAsync(companyId, req, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
