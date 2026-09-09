using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfileAndEmployment;

internal sealed class Endpoint(
    UpdateEmployeeProfileAndEmploymentHandler handler, ICurrentUser currentUser)
    : Endpoint<UpdateEmployeeProfileAndEmploymentRequest, UpdateEmployeeProfileAndEmploymentResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{id:guid}/profile-and-employment");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        UpdateEmployeeProfileAndEmploymentRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
