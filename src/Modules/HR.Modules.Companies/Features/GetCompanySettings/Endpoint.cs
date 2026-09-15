using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCompanySettings;

internal sealed class Endpoint(
    GetCompanySettingsHandler handler) : Endpoint<GetCompanySettingsRequest, GetCompanySettingsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/settings");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetCompanySettingsRequest request,
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
