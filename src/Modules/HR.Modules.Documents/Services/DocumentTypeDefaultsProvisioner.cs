using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class DocumentTypeDefaultsProvisioner(DocumentsDbContext dbContext, IClock clock) : IDocumentTypeDefaultsProvisioner
{
    public async Task EnsureDefaultDocumentTypesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.DocumentTypes.AnyAsync(dt => dt.CompanyId == companyId, cancellationToken))
            return;

        var now = clock.UtcNowOffset();

        dbContext.DocumentTypes.AddRange(
            DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Driving Licence", null, now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Right To Work", null, now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Certificate", null, now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Other", null, now));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
