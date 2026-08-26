using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetUserAuditHistory;

internal sealed class Endpoint(GetUserAuditHistoryHandler handler) : Endpoint<GetUserAuditHistoryRequest, GetUserAuditHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/{employeeId:guid}/audit-history");
        Policies("users:view");
    }

    public override async Task HandleAsync(GetUserAuditHistoryRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
