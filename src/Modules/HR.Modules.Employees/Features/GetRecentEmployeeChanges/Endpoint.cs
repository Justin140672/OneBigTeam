using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetRecentEmployeeChanges;

internal sealed class Endpoint(GetRecentEmployeeChangesHandler handler)
    : EndpointWithoutRequest<GetRecentEmployeeChangesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/recent-changes");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId = Route<Guid>("companyId");

        var result = await handler.HandleAsync(companyId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
