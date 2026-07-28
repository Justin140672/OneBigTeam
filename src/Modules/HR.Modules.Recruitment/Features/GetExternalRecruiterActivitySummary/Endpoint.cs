using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;

internal sealed class Endpoint(GetExternalRecruiterActivitySummaryHandler handler)
    : Endpoint<GetExternalRecruiterActivitySummaryRequest, GetExternalRecruiterActivitySummaryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/external-recruiters/{externalRecruiterId:guid}/activity-summary");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        GetExternalRecruiterActivitySummaryRequest request,
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
