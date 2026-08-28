using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCompanyAuditLog;

/// <summary>
/// AUD-05: GET /api/companies/{companyId}/audit-log
/// Accessible to HR Administrators (employee:manage policy).
/// Query parameters: employeeId, eventType, fromDate, toDate, pageNumber, pageSize.
/// </summary>
internal sealed class Endpoint(
    GetCompanyAuditLogHandler handler) : Endpoint<GetCompanyAuditLogRequest, GetCompanyAuditLogResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/audit-log");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetCompanyAuditLogRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
