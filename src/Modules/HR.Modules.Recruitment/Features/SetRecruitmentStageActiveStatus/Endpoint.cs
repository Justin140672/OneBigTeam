using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.SetRecruitmentStageActiveStatus;

internal sealed class Endpoint(SetRecruitmentStageActiveStatusHandler handler)
    : Endpoint<SetRecruitmentStageActiveStatusRequest, SetRecruitmentStageActiveStatusResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/recruitment-stages/{recruitmentStageId:guid}/active-status");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        SetRecruitmentStageActiveStatusRequest request,
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

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
