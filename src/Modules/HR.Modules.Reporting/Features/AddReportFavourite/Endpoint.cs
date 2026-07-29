using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.AddReportFavourite;

internal sealed class Endpoint(AddReportFavouriteHandler handler, ICurrentUser currentUser)
    : Endpoint<AddReportFavouriteRequest, AddReportFavouriteResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/reporting/favourites/{reportId}");
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        AddReportFavouriteRequest request,
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
