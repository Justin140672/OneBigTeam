using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetPlatformSettings;

internal sealed class Endpoint(
    GetPlatformSettingsHandler handler) : Endpoint<GetPlatformSettingsRequest, GetPlatformSettingsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/platform-settings");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetPlatformSettingsRequest req, CancellationToken cancellationToken)
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
