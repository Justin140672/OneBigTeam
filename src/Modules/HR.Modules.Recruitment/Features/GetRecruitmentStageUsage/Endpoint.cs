using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetRecruitmentStageUsage;

internal sealed class Endpoint(GetRecruitmentStageUsageHandler handler)
    : Endpoint<GetRecruitmentStageUsageRequest, GetRecruitmentStageUsageResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment-stages/{recruitmentStageId:guid}/usage");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        GetRecruitmentStageUsageRequest request,
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
