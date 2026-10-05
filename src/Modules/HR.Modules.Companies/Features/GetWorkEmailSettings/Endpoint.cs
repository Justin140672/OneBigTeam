using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetWorkEmailSettings;

internal sealed class Endpoint(
    GetWorkEmailSettingsHandler handler) : Endpoint<GetWorkEmailSettingsRequest, GetWorkEmailSettingsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/work-email-settings");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        GetWorkEmailSettingsRequest request,
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
