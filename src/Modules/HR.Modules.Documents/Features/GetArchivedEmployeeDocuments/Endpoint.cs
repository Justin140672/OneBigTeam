using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetArchivedEmployeeDocuments;

// DOC-04: "authorised HR users can view ... archived documents" — deliberately gated by
// "employee:manage" (HrAdministrator-only) plus the centralised DocumentResourceAuthorizer's
// IsHrAdministratorAsync check, narrower than the self/manager-hierarchy scope
// CanAccessEmployeeDocumentsAsync grants for normal (non-archived) document access. A manager is
// never allowed to view a direct report's archived documents through this endpoint.
internal sealed class Endpoint(
    GetArchivedEmployeeDocumentsHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer) : Endpoint<GetArchivedEmployeeDocumentsRequest, GetArchivedEmployeeDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/archived");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetArchivedEmployeeDocumentsRequest request,
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
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
