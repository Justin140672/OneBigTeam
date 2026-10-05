using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.AdjudicateTaskCompletionOperation;

internal sealed class Endpoint(AdjudicateTaskCompletionOperationHandler handler, ICurrentUser currentUser)
    : Endpoint<AdjudicateTaskCompletionOperationRequest, AdjudicateTaskCompletionOperationResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/tasks/completion-operations/{operationId:guid}/adjudicate");
        Policies("company:manage");
    }

    public override async Task HandleAsync(
        AdjudicateTaskCompletionOperationRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } operatorUserId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, operatorUserId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
