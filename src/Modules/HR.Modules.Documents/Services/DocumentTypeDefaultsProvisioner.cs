using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Implements IDocumentTypeDefaultsProvisioner — see the interface doc comment in
/// HR.Infrastructure.Abstractions for why this exists. Mirrors DocumentsModule's dev seed set so
/// production provisioning never drifts out of sync with what the dev/E2E environment already
/// treats as "correct".
/// </summary>
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
