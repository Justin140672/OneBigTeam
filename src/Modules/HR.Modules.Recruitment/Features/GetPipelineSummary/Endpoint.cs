using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetPipelineSummary;

internal sealed class Endpoint(
    GetPipelineSummaryHandler handler) : Endpoint<GetPipelineSummaryRequest, GetPipelineSummaryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/pipeline-summary");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetPipelineSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
