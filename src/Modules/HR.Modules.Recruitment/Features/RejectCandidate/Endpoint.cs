using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed class Endpoint(RejectCandidateHandler handler)
    : Endpoint<RejectCandidateRequest, RejectCandidateResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/reject");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        RejectCandidateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var performedBy))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, performedBy, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
