using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// HR dashboard "Needs your attention" queue provider for Shared Company Documents whose
/// ReviewDate has arrived or is within the next 7 days (mirrors
/// ListSharedCompanyDocumentsDueForReviewHandler's own overdue/due-this-week window, which powers
/// the standalone Document Reviews report). HR-only.
///
/// Unlike DetectDocumentsDueForReviewJob (a daily background job that only creates a Review task
/// for documents that already have a ReviewOwnerEmployeeId), this provider surfaces every document
/// due for review — including ones with no owner assigned yet — directly from
/// SharedCompanyDocuments so the widget reflects a document's review-due state immediately, without
/// waiting for the next job run or requiring an owner.
/// </summary>
internal sealed class SharedCompanyDocumentReviewsDueWorkloadActionProvider(
    DocumentsDbContext db,
    IClock clock,
    Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Document review";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
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

        // ActionType carries the document's own title — this becomes the row's subject/title
        // (AttentionQueueWidget.AttentionItem.ActionTitle), matching the standalone Document
        // Reviews report where each row is identified by document title, not by an employee.
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
