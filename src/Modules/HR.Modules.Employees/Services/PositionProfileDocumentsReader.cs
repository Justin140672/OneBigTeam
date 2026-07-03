using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class PositionProfileDocumentsReader(EmployeesDbContext dbContext)
    : IPositionProfileDocumentsReader
{
    public async Task<IReadOnlyList<PositionProfileRequiredDocumentItem>> GetActiveDocumentsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken)
    {
        return await dbContext.PositionProfileRequiredDocuments
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId
                     && d.PositionProfileId == positionProfileId
                     && d.IsActive)
            .Select(d => new PositionProfileRequiredDocumentItem(
                d.Id,
                d.DocumentTypeId,
                d.IsMandatory,
                d.DueDaysAfterStart,
                d.RequiresExpiryDate))
            .ToListAsync(cancellationToken);
    }
}
