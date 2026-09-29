using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.SearchEmployeeDocuments;

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

        var isHrAdministrator = await authorizer.IsHrAdministratorAsync(callerId, cancellationToken);

        IReadOnlyCollection<Guid>? allowedEmployeeIds = null;
        if (!isHrAdministrator)
        {
            var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
                request.CompanyId, callerId, cancellationToken);

            var scope = new HashSet<Guid>(descendantIds) { callerId };
            allowedEmployeeIds = scope;
        }

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
