using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.CreateLeaveType;

internal sealed class Endpoint(CreateLeaveTypeHandler handler, ICurrentUser currentUser)
    : Endpoint<CreateLeaveTypeRequest, CreateLeaveTypeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/leave-types");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CreateLeaveTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/leave-types/{result.Value!.Id}", result.Value));
    }
}
