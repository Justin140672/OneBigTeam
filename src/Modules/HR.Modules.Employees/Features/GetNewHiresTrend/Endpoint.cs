using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetNewHiresTrend;

internal sealed class Endpoint(
    GetNewHiresTrendHandler handler) : Endpoint<GetNewHiresTrendRequest, GetNewHiresTrendResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/new-hires-trend");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetNewHiresTrendRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
