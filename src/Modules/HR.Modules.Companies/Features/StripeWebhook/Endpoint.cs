using FastEndpoints;

using Microsoft.AspNetCore.Http;

using Stripe;

namespace HR.Modules.Companies.Features.StripeWebhook;

// Public (anonymous) Stripe webhook receiver — verified via the Stripe-Signature header rather
// than platform authentication, same rationale as Identity's SignUp/AcceptInvite endpoints.
// EndpointWithoutRequest is used deliberately so FastEndpoints never attempts JSON model binding
// against the body, which would consume the stream before the raw payload can be read for
// signature verification.
internal sealed class Endpoint(
    StripeWebhookHandler handler) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/companies/stripe-webhook");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(HttpContext.Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signatureHeader = HttpContext.Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrEmpty(signatureHeader))
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        try
        {
            await handler.HandleAsync(payload, signatureHeader, cancellationToken);
        }
        catch (StripeException)
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok());
    }
}
