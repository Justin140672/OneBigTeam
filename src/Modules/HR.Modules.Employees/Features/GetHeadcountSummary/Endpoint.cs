using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetHeadcountSummary;

internal sealed class Endpoint(
    GetHeadcountSummaryHandler handler) : Endpoint<GetHeadcountSummaryRequest, GetHeadcountSummaryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/headcount-summary");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetHeadcountSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
