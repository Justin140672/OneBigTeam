using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.ListMarketingContent;

internal sealed class Endpoint(ListMarketingContentHandler handler)
    : Endpoint<ListMarketingContentRequest, ListMarketingContentResponse>
{
    public override void Configure()
    {
        Get("/api/marketing/admin/content");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ListMarketingContentRequest req, CancellationToken cancellationToken)
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
