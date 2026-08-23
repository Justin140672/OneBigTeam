using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class Endpoint(
    ApproveLeaveRequestHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<ApproveLeaveRequestRequest, ApproveLeaveRequestResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/{leaveRequestId:guid}/approve");
        Policies("leave:approve");
    }

    public override async Task HandleAsync(
        ApproveLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } reviewerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // SEC: the acting reviewer must always be the authenticated caller, never trusted from
        // request data — any client-supplied ReviewedByEmployeeId is discarded here and replaced
        // with the server-resolved identity before authorization or persistence.
        request = request with { ReviewedByEmployeeId = reviewerId };

        // LEAVE-01: only HR Administrators or a manager anywhere above the target employee in
        // the reporting hierarchy may approve.
        if (!await authorizer.CanApproveOrRejectAsync(request.CompanyId, reviewerId, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

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

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
