using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetAuditLog;

internal sealed class Endpoint(
    GetAuditLogHandler handler) : Endpoint<GetAuditLogRequest, GetAuditLogResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/audit-log");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetAuditLogRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

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
