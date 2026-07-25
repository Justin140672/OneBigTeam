using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;

internal sealed class Endpoint(BulkApplyCompensationAdjustmentsHandler handler)
    : Endpoint<BulkApplyCompensationAdjustmentsRequest, BulkApplyCompensationAdjustmentsResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/compensation/bulk");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        BulkApplyCompensationAdjustmentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var actorEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

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
