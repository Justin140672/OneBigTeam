using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.PreviewLeaveRequest;

internal sealed class Endpoint(
    PreviewLeaveRequestHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer)
    : Endpoint<PreviewLeaveRequestRequest, PreviewLeaveRequestResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/preview");
        Policies("leave:request");
    }

    public override async Task HandleAsync(
        PreviewLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        // LEAVE-01: preview mirrors submit's self-service-only scope.
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!await authorizer.CanActOnOwnLeaveAsync(callerId, request.EmployeeId, cancellationToken))
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
