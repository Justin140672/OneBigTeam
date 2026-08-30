using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEmploymentTypeSplit;

internal sealed class Endpoint(
    GetEmploymentTypeSplitHandler handler) : Endpoint<GetEmploymentTypeSplitRequest, GetEmploymentTypeSplitResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/employment-type-split");
        // ADM-05: workforce analytics — Manager / Recruiter / HR Administrator only.
        Policies("employee:read");
    }

    public override async Task HandleAsync(
        GetEmploymentTypeSplitRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
