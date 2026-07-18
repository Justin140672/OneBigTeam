using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ApplyPositionProfileMatches;

internal sealed class Endpoint(ApplyPositionProfileMatchesHandler handler)
    : Endpoint<ApplyPositionProfileMatchesRequest, ApplyPositionProfileMatchesResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/position-profile-matches/apply");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        ApplyPositionProfileMatchesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
