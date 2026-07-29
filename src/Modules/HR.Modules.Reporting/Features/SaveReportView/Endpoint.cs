using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.SaveReportView;

internal sealed class Endpoint(SaveReportViewHandler handler, ICurrentUser currentUser)
    : Endpoint<SaveReportViewRequest, SaveReportViewResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/reporting/saved-views");
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        SaveReportViewRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, userId, cancellationToken);

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
