using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed class Endpoint(
    InviteEmployeeUserHandler handler,
    ICurrentUser currentUser) : Endpoint<InviteEmployeeUserRequest, InviteEmployeeUserResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/invite-user");
        Policies("users:manage");
    }

    public override async Task HandleAsync(InviteEmployeeUserRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, currentUser.UserId, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
