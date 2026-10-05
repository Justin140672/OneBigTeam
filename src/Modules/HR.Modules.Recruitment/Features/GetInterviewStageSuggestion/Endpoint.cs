using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetInterviewStageSuggestion;

internal sealed class Endpoint(GetInterviewStageSuggestionHandler handler, ICurrentUser currentUser)
    : Endpoint<GetInterviewStageSuggestionRequest, GetInterviewStageSuggestionResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment-stages/interview-stage-suggestion");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        GetInterviewStageSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
