using FastEndpoints;
using HR.Modules.Reporting.ReportRegistry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.AddReportFavourite;

internal sealed class Endpoint(
    AddReportFavouriteHandler handler,
    HR.SharedKernel.ICurrentUser currentUser,
    IAuthorizationService authorizationService) : Endpoint<AddReportFavouriteRequest, AddReportFavouriteResponse>
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

        var accessGates = await ReportAccessGateEvaluator.EvaluateAsync(authorizationService, User);

        var result = await handler.HandleAsync(request, userId, accessGates, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "forbidden")
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
