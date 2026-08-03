using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetGenderSplit;

internal sealed class Endpoint(
    GetGenderSplitHandler handler) : Endpoint<GetGenderSplitRequest, GetGenderSplitResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/gender-split");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetGenderSplitRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
