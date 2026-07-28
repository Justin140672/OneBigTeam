using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiterUsage;

internal sealed class Endpoint(GetExternalRecruiterUsageHandler handler)
    : Endpoint<GetExternalRecruiterUsageRequest, GetExternalRecruiterUsageResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/external-recruiters/{externalRecruiterId:guid}/usage");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        GetExternalRecruiterUsageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
