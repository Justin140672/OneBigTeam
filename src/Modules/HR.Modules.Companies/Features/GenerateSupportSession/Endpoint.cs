using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GenerateSupportSession;

internal sealed class Endpoint(
    GenerateSupportSessionHandler handler) : Endpoint<GenerateSupportSessionRequest, GenerateSupportSessionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/admin/customers/{companyId:guid}/support-session");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GenerateSupportSessionRequest req, CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up item 5: deliberately NOT idempotency-wrapped. The response
        // carries a live, single-issue bearer token - replaying a stored copy would both leak that
        // secret into the idempotency table in plaintext (the business table only ever stores its
        // hash) and hand back a session that may since have been revoked. A retry here should mint
        // a fresh session, not replay an old one.
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
