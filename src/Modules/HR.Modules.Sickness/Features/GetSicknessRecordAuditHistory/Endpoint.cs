using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetSicknessRecordAuditHistory;

internal sealed class Endpoint(GetSicknessRecordAuditHistoryHandler handler)
    : EndpointWithoutRequest<GetSicknessRecordAuditHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/sickness-records/{sicknessRecordId:guid}/audit-history");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId        = Route<Guid>("companyId");
        var sicknessRecordId = Route<Guid>("sicknessRecordId");

        var result = await handler.HandleAsync(companyId, sicknessRecordId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
