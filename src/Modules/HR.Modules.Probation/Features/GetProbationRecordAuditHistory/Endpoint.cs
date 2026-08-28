using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetProbationRecordAuditHistory;

internal sealed class Endpoint(GetProbationRecordAuditHistoryHandler handler)
    : EndpointWithoutRequest<GetProbationRecordAuditHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-records/{probationRecordId:guid}/audit-history");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId         = Route<Guid>("companyId");
        var probationRecordId = Route<Guid>("probationRecordId");

        var result = await handler.HandleAsync(companyId, probationRecordId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
