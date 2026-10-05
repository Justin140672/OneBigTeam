using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListBlockedInterviewOutcomeReconciliations;

internal sealed class Endpoint(ListBlockedInterviewOutcomeReconciliationsHandler handler)
    : Endpoint<ListBlockedInterviewOutcomeReconciliationsRequest, ListBlockedInterviewOutcomeReconciliationsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/interview-outcome-reconciliations/blocked");
        Policies("company:manage");
    }

    public override async Task HandleAsync(
        ListBlockedInterviewOutcomeReconciliationsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
