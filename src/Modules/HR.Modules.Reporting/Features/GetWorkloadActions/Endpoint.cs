using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetWorkloadActions;

internal sealed class Endpoint(GetWorkloadActionsHandler handler)
    : Endpoint<GetWorkloadActionsRequest, GetWorkloadActionsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/workload-actions");
        // Uses the dedicated reporting:view-workload-actions policy (Manager, HrAdministrator only)
        // — the same access gate the catalogue resolves via ReportAccessGate.WorkloadActions /
        // ReportAccessGateEvaluator, so a Recruiter-only or Company Administrator (without an
        // operational HR role) caller is rejected with 403 before the handler ever runs. Per-category
        // row-level scoping still happens inside each IWorkloadActionProvider (see Handler.cs xmldoc)
        // as defence in depth on top of this endpoint-level check.
        Policies("reporting:view-workload-actions");
    }

    public override async Task HandleAsync(
        GetWorkloadActionsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, User, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
