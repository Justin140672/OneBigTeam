using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCompanySettingsHistory;

internal sealed class Endpoint(
    GetCompanySettingsHistoryHandler handler) : Endpoint<GetCompanySettingsHistoryRequest, GetCompanySettingsHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/settings/history");
        Policies("company:manage");
    }

    public override async Task HandleAsync(
        GetCompanySettingsHistoryRequest request,
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
