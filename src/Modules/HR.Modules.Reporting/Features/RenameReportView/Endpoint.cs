using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.RenameReportView;

internal sealed class Endpoint(RenameReportViewHandler handler, ICurrentUser currentUser)
    : Endpoint<RenameReportViewRequest, RenameReportViewResponse>
{
    public override void Configure()
    {
        Patch("/api/companies/{companyId:guid}/reporting/saved-views/{viewId:guid}");
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        RenameReportViewRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, userId, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
