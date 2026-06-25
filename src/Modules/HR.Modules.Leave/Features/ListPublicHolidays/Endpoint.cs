using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.ListPublicHolidays;

internal sealed class Endpoint(
    ListPublicHolidaysHandler handler) : Endpoint<ListPublicHolidaysRequest, ListPublicHolidaysResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/public-holidays");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListPublicHolidaysRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
