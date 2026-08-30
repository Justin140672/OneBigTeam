using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.MarkAdministrativeAlertRead;

internal sealed class Endpoint(MarkAdministrativeAlertReadHandler handler, ICurrentUser currentUser)
    : Endpoint<MarkAdministrativeAlertReadRequest>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/administrative-alerts/{alertId:guid}/read");
        Policies("admin-alerts:view");
    }

    public override async Task HandleAsync(MarkAdministrativeAlertReadRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { })
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.NoContent());
    }
}
