using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;

internal sealed class Endpoint(
    GetEmployeeDocumentVersionHistoryHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer,
    IAuditEventPublisher auditPublisher,
    IClock clock) : Endpoint<GetEmployeeDocumentVersionHistoryRequest, GetEmployeeDocumentVersionHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}/versions");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetEmployeeDocumentVersionHistoryRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        if (!await authorizer.IsHrAdministratorAsync(callerId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await auditPublisher.PublishAsync(new EmployeeDocumentVersionHistoryViewedAuditEvent(
            request.CompanyId,
            request.EmployeeDocumentId,
            request.EmployeeId,
            result.Value!.Versions.Count,
            callerId,
            clock.UtcNowOffset()), cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
