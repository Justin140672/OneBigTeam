using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.GetAdministrativeAlerts;

internal sealed class Endpoint(GetAdministrativeAlertsHandler handler, ICurrentUser currentUser)
    : Endpoint<GetAdministrativeAlertsRequest, GetAdministrativeAlertsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/administrative-alerts");
        Policies("admin-alerts:view");
    }

    public override async Task HandleAsync(GetAdministrativeAlertsRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { })
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
