using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetLeaveRequest;

internal sealed class Endpoint(
    GetLeaveRequestHandler handler) : Endpoint<GetLeaveRequestRequest, GetLeaveRequestResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
