using FastEndpoints;

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
