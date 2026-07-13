using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Persistence;

internal class DocumentsDbContext : DbContext
{
    public DocumentsDbContext(DbContextOptions<DocumentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<DocumentRequest> DocumentRequests => Set<DocumentRequest>();
    public DbSet<EmployeeProfilePhoto> EmployeeProfilePhotos => Set<EmployeeProfilePhoto>();
    public DbSet<PendingProfilePhoto> PendingProfilePhotos => Set<PendingProfilePhoto>();
    public DbSet<SharedCompanyDocument> SharedCompanyDocuments => Set<SharedCompanyDocument>();
    public DbSet<CompanyDocumentCategory> CompanyDocumentCategories => Set<CompanyDocumentCategory>();
    public DbSet<SharedCompanyDocumentVersion> SharedCompanyDocumentVersions => Set<SharedCompanyDocumentVersion>();
    public DbSet<SharedCompanyDocumentAcknowledgement> SharedCompanyDocumentAcknowledgements => Set<SharedCompanyDocumentAcknowledgement>();
    public DbSet<SharedCompanyDocumentAudienceRule> SharedCompanyDocumentAudienceRules => Set<SharedCompanyDocumentAudienceRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("documents");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentsDbContext).Assembly);
    }
}
