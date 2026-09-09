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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
