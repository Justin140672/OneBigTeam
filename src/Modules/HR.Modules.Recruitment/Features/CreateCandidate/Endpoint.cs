using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateCandidate;

internal sealed class Endpoint(CreateCandidateHandler handler)
    : Endpoint<CreateCandidateRequest, CreateCandidateResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/candidates");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        CreateCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/candidates/{result.Value.Id}",
            result.Value));
    }
}
