using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.RemoveReportFavourite;

internal sealed class Endpoint(RemoveReportFavouriteHandler handler, ICurrentUser currentUser)
    : Endpoint<RemoveReportFavouriteRequest, RemoveReportFavouriteResponse>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/reporting/favourites/{reportId}");
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        RemoveReportFavouriteRequest request,
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
