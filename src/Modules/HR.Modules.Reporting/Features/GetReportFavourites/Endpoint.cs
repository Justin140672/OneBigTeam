using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetReportFavourites;

internal sealed class Endpoint(GetReportFavouritesHandler handler, ICurrentUser currentUser)
    : Endpoint<GetReportFavouritesRequest, GetReportFavouritesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/favourites");
        // Any reporting-entitled user can favourite/see their own favourites among the reports
        // they can already see — same baseline gate as the catalog itself.
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        GetReportFavouritesRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, userId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
