using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.GetAdministrativeAlertUnreadCount;

internal sealed class Endpoint(GetAdministrativeAlertUnreadCountHandler handler, ICurrentUser currentUser)
    : Endpoint<GetAdministrativeAlertUnreadCountRequest, GetAdministrativeAlertUnreadCountResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/administrative-alerts/unread-count");
        Policies("admin-alerts:view");
    }

    public override async Task HandleAsync(GetAdministrativeAlertUnreadCountRequest request, CancellationToken cancellationToken)
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
