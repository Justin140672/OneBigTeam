using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.SearchEmployeeDocuments;

// DOC-06: company-wide document search, distinct from ListEmployeeDocuments (per-employee route).
// Access scope is resolved here (not trusted from the request) and passed into the handler:
//   - HR Administrator          -> unrestricted company-wide (allowedEmployeeIds = null)
//   - Manager                   -> self + complete reporting hierarchy (DirectReportsReader)
//   - Anyone else (Employee)    -> self only
// This mirrors DocumentResourceAuthorizer.CanAccessEmployeeDocumentsAsync's per-employee scope
// rules (DOC-01), applied here across every result row instead of a single target employee.
internal sealed class Endpoint(
    SearchEmployeeDocumentsHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer,
    IDirectReportsReader directReportsReader) : Endpoint<SearchEmployeeDocumentsRequest, SearchEmployeeDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/documents/search");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        SearchEmployeeDocumentsRequest request,
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

        // Same convention as ListEmployeeDocuments/GetArchivedEmployeeDocuments: ICurrentUser.UserId
        // is used directly as the caller's employee id (DocumentResourceAuthorizer's
        // "callerEmployeeId" parameter), no separate user-to-employee lookup exists in this module.
        var isHrAdministrator = await authorizer.IsHrAdministratorAsync(callerId, cancellationToken);

        IReadOnlyCollection<Guid>? allowedEmployeeIds = null;
        if (!isHrAdministrator)
        {
            var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
                request.CompanyId, callerId, cancellationToken);

            var scope = new HashSet<Guid>(descendantIds) { callerId };
            allowedEmployeeIds = scope;
        }

        // If a specific employeeId filter was requested, it must itself be within the caller's
        // access scope — otherwise a manager/employee could probe for another employee's
        // documents purely via the filter even though the base scope excludes them.
        if (request.EmployeeId is Guid requestedEmployeeId
            && allowedEmployeeIds is not null
            && !allowedEmployeeIds.Contains(requestedEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, allowedEmployeeIds, isHrAdministrator, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
