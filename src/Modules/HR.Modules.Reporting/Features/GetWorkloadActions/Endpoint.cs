using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetWorkloadActions;

internal sealed class Endpoint(GetWorkloadActionsHandler handler)
    : Endpoint<GetWorkloadActionsRequest, GetWorkloadActionsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/workload-actions");
        // Baseline reporting:view gate only (Manager, Recruiter, HrAdministrator) — same
        // defense-in-depth pattern as GetReportCatalog. The real, per-category permission scoping
        // happens inside each IWorkloadActionProvider (see Handler.cs xmldoc): a Manager only ever
        // gets their own direct reports' items, a Recruiter only gets recruitment categories, and a
        // caller with none of the baseline roles is rejected outright by this policy before the
        // handler ever runs.
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        GetWorkloadActionsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, User, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
