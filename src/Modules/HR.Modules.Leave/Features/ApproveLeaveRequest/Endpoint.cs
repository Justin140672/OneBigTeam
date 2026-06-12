using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class Endpoint(
    ApproveLeaveRequestHandler handler) : Endpoint<ApproveLeaveRequestRequest, ApproveLeaveRequestResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/{leaveRequestId:guid}/approve");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ApproveLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
