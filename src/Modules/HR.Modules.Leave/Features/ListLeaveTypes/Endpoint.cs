using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.ListLeaveTypes;

internal sealed class Endpoint(ListLeaveTypesHandler handler)
    : Endpoint<ListLeaveTypesRequest, ListLeaveTypesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/leave-types");
        Policies("role:employee");
    }

    public override async Task HandleAsync(ListLeaveTypesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
