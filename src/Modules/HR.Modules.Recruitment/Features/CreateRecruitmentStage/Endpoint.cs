using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateRecruitmentStage;

internal sealed class Endpoint(CreateRecruitmentStageHandler handler)
    : Endpoint<CreateRecruitmentStageRequest, CreateRecruitmentStageResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/recruitment-stages");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        CreateRecruitmentStageRequest request,
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
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/recruitment-stages/{result.Value.Id}",
            result.Value));
    }
}
