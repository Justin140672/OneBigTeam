using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.GetSupportRequest;

internal sealed class Endpoint(GetSupportRequestHandler handler)
    : Endpoint<GetSupportRequestRequest, GetSupportRequestResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/support/requests/{id:guid}");
        Policies("support:manage");
    }

    public override async Task HandleAsync(GetSupportRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
