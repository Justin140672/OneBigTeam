using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

internal sealed class Endpoint(
    UpdateRecruitmentSettingsHandler handler) : Endpoint<UpdateRecruitmentSettingsRequest, UpdateRecruitmentSettingsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/recruitment-settings");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        UpdateRecruitmentSettingsRequest request,
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
