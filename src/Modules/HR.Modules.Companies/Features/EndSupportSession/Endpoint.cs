using FastEndpoints;

using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.EndSupportSession;

internal sealed class Endpoint(
    EndSupportSessionHandler handler) : EndpointWithoutRequest<EndSupportSessionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/support-session/end");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EndSupportSessionRequest(), cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
