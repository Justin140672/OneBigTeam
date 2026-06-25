using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetProbationRecord;

internal sealed class Endpoint(
    GetProbationRecordHandler handler) : Endpoint<GetProbationRecordRequest, GetProbationRecordResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-records/{id:guid}");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        GetProbationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
