using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetHrSettingsHistory;

internal sealed class Endpoint(
    GetHrSettingsHistoryHandler handler) : Endpoint<GetHrSettingsHistoryRequest, GetHrSettingsHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/hr-settings/history");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        GetHrSettingsHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
