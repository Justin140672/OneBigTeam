using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.ListBackgroundJobs;

internal sealed class Endpoint(
    ListBackgroundJobsHandler handler) : EndpointWithoutRequest<ListBackgroundJobsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/background-jobs");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListBackgroundJobsRequest(), cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
