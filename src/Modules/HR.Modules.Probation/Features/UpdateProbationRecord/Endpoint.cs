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
            // Ticket 18: preserve the structured error code (concurrency vs. ordinary business
            // conflict) via the shared ProblemResults translator so HR.Web can tell a stale
            // ExpectedVersion apart from a terminal-status rejection instead of treating every
            // HTTP 409 as a concurrency conflict.
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
