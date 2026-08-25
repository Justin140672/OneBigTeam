using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.UpdateProbationRecord;

internal sealed class Endpoint(
    UpdateProbationRecordHandler handler,
    ICurrentUser currentUser) : Endpoint<UpdateProbationRecordRequest, UpdateProbationRecordResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/probation-records/{id:guid}");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        UpdateProbationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
