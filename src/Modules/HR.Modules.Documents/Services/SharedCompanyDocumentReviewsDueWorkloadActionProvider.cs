using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class SharedCompanyDocumentReviewsDueWorkloadActionProvider(
    DocumentsDbContext db,
    IClock clock,
    Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Document review";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        if (requestedScope != WorkloadScope.Hr)
            return [];

        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var dueBy = today.AddDays(7);

        var documents = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId
                && d.Status != SharedCompanyDocumentStatus.Archived
                && d.Status != SharedCompanyDocumentStatus.Expired
                && d.ReviewDate != null
                && d.ReviewDate <= dueBy)
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
            return [];

        return documents.Select(d => new WorkloadAction(
            EmployeeId: d.ReviewOwnerEmployeeId ?? Guid.Empty,
            EmployeeName: "",
            Department: null,
            ActionType: d.Title,
            ActionCategory: ActionCategory,
            DueDate: d.ReviewDate,
            AssignedTo: null,
            Status: d.ReviewDate < today ? "Overdue" : "Due This Week",
            DeepLinkUrl: $"/companies/{companyId}/shared-documents/{d.Id}")).ToList();
    }
}
