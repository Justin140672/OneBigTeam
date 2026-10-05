using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.ResetProgrammaticTaskCompletion;

internal sealed class Endpoint(ResetProgrammaticTaskCompletionHandler handler, ICurrentUser currentUser)
    : Endpoint<ResetProgrammaticTaskCompletionRequest, ResetProgrammaticTaskCompletionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/tasks/programmatic-completions/{operationId:guid}/reset");
        Policies("company:manage");
    }

    public override async Task HandleAsync(
        ResetProgrammaticTaskCompletionRequest request,
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
