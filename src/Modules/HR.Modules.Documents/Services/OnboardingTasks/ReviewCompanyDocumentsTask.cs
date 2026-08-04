using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services.OnboardingTasks;

internal sealed class ReviewCompanyDocumentsTask(DocumentsDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "review-company-documents";
    public string Name => "Review your company documents";
    public string Description => "Publish at least one company policy or handbook document.";
    public bool IsMandatory => true;
    // HR.Web's shared company documents route is company-scoped
    // ("/companies/{CompanyId:guid}/shared-documents") — the "{companyId}" placeholder is
    // substituted by HR.Web with the current company id.
    public string LinkUrl => "/companies/{companyId}/shared-documents";
    public int Order => 6;

    public Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return dbContext.SharedCompanyDocuments
            .AsNoTracking()
            .AnyAsync(d => d.CompanyId == companyId && d.Status == SharedCompanyDocumentStatus.Published, cancellationToken);
    }
}
