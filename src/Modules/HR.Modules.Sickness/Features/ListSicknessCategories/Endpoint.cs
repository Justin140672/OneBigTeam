using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.ListSicknessCategories;

internal sealed class Endpoint(ListSicknessCategoriesHandler handler)
    : Endpoint<ListSicknessCategoriesRequest, List<ListSicknessCategoriesResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/sickness-categories");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(ListSicknessCategoriesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
