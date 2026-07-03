using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListRequiredDocumentsForPositionProfile;

internal sealed class ListRequiredDocumentsHandler(
    EmployeesDbContext dbContext,
    IDocumentTypeReader documentTypeReader)
{
    public async Task<Result<ListRequiredDocumentsResponse>> HandleAsync(
        ListRequiredDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<ListRequiredDocumentsResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var rows = await dbContext.PositionProfileRequiredDocuments
            .AsNoTracking()
            .Where(d => d.PositionProfileId == request.PositionProfileId
                     && d.CompanyId == request.CompanyId
                     && d.IsActive)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new { d.Id, d.DocumentTypeId, d.IsMandatory, d.DueDaysAfterStart, d.RequiresExpiryDate })
            .ToListAsync(cancellationToken);

        var names = await documentTypeReader.GetNamesAsync(
            request.CompanyId,
            rows.Select(r => r.DocumentTypeId),
            cancellationToken);

        var items = rows.Select(r => new RequiredDocumentListItem(
            r.Id,
            r.DocumentTypeId,
            names.GetValueOrDefault(r.DocumentTypeId, string.Empty),
            r.IsMandatory,
            r.DueDaysAfterStart,
            r.RequiresExpiryDate)).ToList();

        return Result.Success(new ListRequiredDocumentsResponse(items));
    }
}
