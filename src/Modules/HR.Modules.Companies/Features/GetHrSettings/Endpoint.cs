using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetHrSettings;

internal sealed class Endpoint(
    GetHrSettingsHandler handler) : Endpoint<GetHrSettingsRequest, GetHrSettingsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/hr-settings");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetHrSettingsRequest request,
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
