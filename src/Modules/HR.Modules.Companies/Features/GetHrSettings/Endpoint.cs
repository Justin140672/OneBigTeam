using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetHrSettings;

internal sealed class Endpoint(
    GetHrSettingsHandler handler) : Endpoint<GetHrSettingsRequest, GetHrSettingsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/hr-settings");
        // Read access mirrors GetCompanySettings' "role:employee" policy — many parts of the app
        // (leave calculations, sickness thresholds, employee numbering previews, document
        // acknowledgement defaults) need to read HR-policy values regardless of role. The actual
        // permissions gap being fixed is on the write side: only HrAdministrator may change these
        // values (see UpdateHrSettings's "hr-settings:manage" policy).
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetHrSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
