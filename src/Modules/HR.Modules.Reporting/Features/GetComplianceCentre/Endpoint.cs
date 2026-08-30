using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetComplianceCentre;

/// <summary>
/// ADM-02: consolidated Compliance Centre for authorised HR administrators. Gated by the
/// compliance:view policy (HR Administrator only — Company Administrator and every other role are
/// rejected with 403 before the handler runs). Every underlying reader is company-scoped by the
/// route company id.
/// </summary>
internal sealed class Endpoint(GetComplianceCentreHandler handler)
    : Endpoint<GetComplianceCentreRequest, GetComplianceCentreResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/compliance-centre");
        Policies("compliance:view");
    }

    public override async Task HandleAsync(
        GetComplianceCentreRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
