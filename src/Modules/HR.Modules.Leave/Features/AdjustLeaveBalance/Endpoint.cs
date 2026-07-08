using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.AdjustLeaveBalance;

internal sealed class Endpoint(AdjustLeaveBalanceHandler handler) : Endpoint<AdjustLeaveBalanceRequest, AdjustLeaveBalanceResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-balance-adjustments");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(AdjustLeaveBalanceRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var adjustedByEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            request with { AdjustedByEmployeeId = adjustedByEmployeeId },
            cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
