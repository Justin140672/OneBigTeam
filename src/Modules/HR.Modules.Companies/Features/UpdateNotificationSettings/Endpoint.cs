using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateNotificationSettings;

internal sealed class Endpoint(
    UpdateNotificationSettingsHandler handler) : Endpoint<UpdateNotificationSettingsRequest, UpdateNotificationSettingsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/notification-settings");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        UpdateNotificationSettingsRequest request,
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
