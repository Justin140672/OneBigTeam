using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCustomerDashboard;

internal sealed class Endpoint(
    GetCustomerDashboardHandler handler) : EndpointWithoutRequest<GetCustomerDashboardResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/customer-dashboard");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
