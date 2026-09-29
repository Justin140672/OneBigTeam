using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetMyProbationStatus;

internal sealed class Endpoint(GetMyProbationStatusHandler handler, ICurrentUser currentUser)
    : EndpointWithoutRequest<GetMyProbationStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/probation-status");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyId = Route<Guid>("companyId");

        var result = await handler.HandleAsync(companyId, employeeId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
