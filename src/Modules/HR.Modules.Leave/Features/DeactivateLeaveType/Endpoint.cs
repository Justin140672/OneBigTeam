using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed class Endpoint(DeactivateLeaveTypeHandler handler)
    : Endpoint<DeactivateLeaveTypeRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/leave-types/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(DeactivateLeaveTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }
        await Send.NoContentAsync(cancellationToken);
    }
}
