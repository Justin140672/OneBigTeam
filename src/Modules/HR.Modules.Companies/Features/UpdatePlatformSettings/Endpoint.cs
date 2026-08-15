using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdatePlatformSettings;

internal sealed class Endpoint(
    UpdatePlatformSettingsHandler handler) : Endpoint<UpdatePlatformSettingsRequest, UpdatePlatformSettingsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/admin/platform-settings");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(UpdatePlatformSettingsRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };
            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
