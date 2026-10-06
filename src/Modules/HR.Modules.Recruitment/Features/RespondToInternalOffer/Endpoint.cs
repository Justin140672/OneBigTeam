using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.RespondToInternalOffer;

internal sealed class Endpoint(RespondToInternalOfferHandler handler, ICurrentUser currentUser)
    : Endpoint<RespondToInternalOfferRequest, RespondToInternalOfferResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/internal-offers/{applicationId:guid}/response");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        RespondToInternalOfferRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (currentUser.IsSupportSession)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
