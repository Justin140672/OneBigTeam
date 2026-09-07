using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.GetMarketingContent;

internal sealed class Endpoint(GetMarketingContentHandler handler)
    : Endpoint<GetMarketingContentRequest, GetMarketingContentResponse>
{
    public override void Configure()
    {
        Get("/api/marketing/content");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetMarketingContentRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
