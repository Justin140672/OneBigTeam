using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateWorkEmailSettings;

internal sealed class Endpoint(
    UpdateWorkEmailSettingsHandler handler) : Endpoint<UpdateWorkEmailSettingsRequest, UpdateWorkEmailSettingsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/work-email-settings");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        UpdateWorkEmailSettingsRequest request,
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
