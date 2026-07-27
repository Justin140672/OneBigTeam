using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.BackfillEmployeeTimeline;

internal sealed class Endpoint(BackfillEmployeeTimelineHandler handler)
    : Endpoint<BackfillEmployeeTimelineRequest, BackfillEmployeeTimelineResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/timeline/backfill");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        BackfillEmployeeTimelineRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
