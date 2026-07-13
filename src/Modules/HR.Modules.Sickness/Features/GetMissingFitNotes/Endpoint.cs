using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed class Endpoint(
    GetMissingFitNotesHandler handler) : Endpoint<GetMissingFitNotesRequest, GetMissingFitNotesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/sickness-evidence-requests/missing");
        // "sickness:review" (Manager + HrAdministrator) rather than "sickness:manage"
        // (HrAdministrator only) — this company-wide read is what backs
        // MissingFitNotesWidget, shown on both the HR and Manager dashboards.
        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        GetMissingFitNotesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
