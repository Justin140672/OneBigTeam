using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.AddInterviewStage;

internal sealed class Endpoint(AddInterviewStageHandler handler, ICurrentUser currentUser)
    : Endpoint<AddInterviewStageRequest, AddInterviewStageResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/recruitment-stages/interview-stages");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        AddInterviewStageRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
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

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/recruitment-stages/{result.Value.Id}",
            result.Value));
    }
}
