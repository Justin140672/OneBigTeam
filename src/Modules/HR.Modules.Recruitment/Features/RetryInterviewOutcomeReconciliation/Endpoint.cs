using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;

internal sealed class Endpoint(RetryInterviewOutcomeReconciliationHandler handler, ICurrentUser currentUser)
    : Endpoint<RetryInterviewOutcomeReconciliationRequest, RetryInterviewOutcomeReconciliationResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/recruitment/interview-outcome-reconciliations/{reconciliationId:guid}/retry");
        Policies("company:manage");
    }

    public override async Task HandleAsync(
        RetryInterviewOutcomeReconciliationRequest request,
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
