using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.UpdateLeaveType;

internal sealed class Endpoint(UpdateLeaveTypeHandler handler, ICurrentUser currentUser)
    : Endpoint<UpdateLeaveTypeRequest, UpdateLeaveTypeResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/leave-types/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(UpdateLeaveTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);
        if (result.IsFailure)
        {
            var body = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(body));
                return;
            }
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(body));
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(body));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
