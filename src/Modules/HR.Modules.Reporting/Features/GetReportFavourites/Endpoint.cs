using FastEndpoints;
using HR.Modules.Reporting.ReportRegistry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetReportFavourites;

internal sealed class Endpoint(
    GetReportFavouritesHandler handler,
    HR.SharedKernel.ICurrentUser currentUser,
    IAuthorizationService authorizationService) : Endpoint<GetReportFavouritesRequest, GetReportFavouritesResponse>
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

        var accessGates = await ReportAccessGateEvaluator.EvaluateAsync(authorizationService, User);

        var result = await handler.HandleAsync(request, userId, accessGates, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
