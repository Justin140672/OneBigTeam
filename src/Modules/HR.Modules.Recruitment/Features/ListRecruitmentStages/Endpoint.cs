using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListRecruitmentStages;

internal sealed class Endpoint(ListRecruitmentStagesHandler handler)
    : Endpoint<ListRecruitmentStagesRequest, ListRecruitmentStagesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment-stages");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        ListRecruitmentStagesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
