using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.RetryBackgroundJob;

internal sealed class Endpoint(
    RetryBackgroundJobHandler handler) : Endpoint<RetryBackgroundJobRequest, RetryBackgroundJobResponse>
{
    public override void Configure()
    {
        Post("/api/companies/admin/background-jobs/{jobId}/retry");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(RetryBackgroundJobRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
