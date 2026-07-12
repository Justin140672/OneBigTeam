using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetProbationStatus;

internal sealed class Endpoint(GetProbationStatusHandler handler)
    : Endpoint<GetProbationStatusRequest, GetProbationStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/probation-status");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        GetProbationStatusRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
