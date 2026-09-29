using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ListInvitableEmployees;

internal sealed class Endpoint(ListInvitableEmployeesHandler handler)
    : Endpoint<ListInvitableEmployeesRequest, ListInvitableEmployeesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/invitable-employees");
        Policies("users:manage");
    }

    public override async Task HandleAsync(ListInvitableEmployeesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
