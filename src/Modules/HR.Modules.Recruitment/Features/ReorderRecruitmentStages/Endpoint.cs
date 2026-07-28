using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ReorderRecruitmentStages;

internal sealed class Endpoint(ReorderRecruitmentStagesHandler handler)
    : Endpoint<ReorderRecruitmentStagesRequest, ReorderRecruitmentStagesResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/recruitment-stages/reorder");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        ReorderRecruitmentStagesRequest request,
        CancellationToken cancellationToken)
    {
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.UnprocessableEntity(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
