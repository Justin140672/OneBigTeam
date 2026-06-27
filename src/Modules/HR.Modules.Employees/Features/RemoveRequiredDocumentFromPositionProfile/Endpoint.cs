using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.RemoveRequiredDocumentFromPositionProfile;

internal sealed class Endpoint(RemoveRequiredDocumentHandler handler) : Endpoint<RemoveRequiredDocumentRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/required-documents/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        RemoveRequiredDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var actorEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
