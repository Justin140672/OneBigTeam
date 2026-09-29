using HR.Modules.Documents.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.SearchEmployeeDocuments;

internal sealed class SearchEmployeeDocumentsHandler(DocumentsDbContext db, IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<SearchEmployeeDocumentsResponse>> HandleAsync(
        SearchEmployeeDocumentsRequest request,
        IReadOnlyCollection<Guid>? allowedEmployeeIds,
        bool callerIsHrAdministrator,
        CancellationToken cancellationToken)
    {
        var query =
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.CompanyId == request.CompanyId
               && ed.IsLatestVersion
            select new { ed, d, dt };

        if (allowedEmployeeIds is not null)
            query = query.Where(x => allowedEmployeeIds.Contains(x.ed.EmployeeId));

        var includeArchived = request.IncludeArchived && callerIsHrAdministrator;
        if (!includeArchived)
            query = query.Where(x => !x.ed.IsArchived);

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.d.Title.ToLower().Contains(search) ||
                x.d.FileName.ToLower().Contains(search));
        }

        if (request.DocumentTypeId is not null)
            query = query.Where(x => x.d.DocumentTypeId == request.DocumentTypeId);

        if (request.EmployeeId is not null)
            query = query.Where(x => x.ed.EmployeeId == request.EmployeeId);

        if (request.Status is not null)
            query = query.Where(x => x.d.Status == request.Status);

        if (request.UploadedBy is not null)
            query = query.Where(x => x.d.UploadedBy == request.UploadedBy);

        if (request.UploadedFrom is not null)
            query = query.Where(x => DateOnly.FromDateTime(x.ed.CreatedAt.Date) >= request.UploadedFrom.Value);

        if (request.UploadedTo is not null)
            query = query.Where(x => DateOnly.FromDateTime(x.ed.CreatedAt.Date) <= request.UploadedTo.Value);

        if (request.ExpiresFrom is not null)
            query = query.Where(x => x.ed.ExpiryDate != null && x.ed.ExpiryDate >= request.ExpiresFrom.Value);

        if (request.ExpiresTo is not null)
            query = query.Where(x => x.ed.ExpiryDate != null && x.ed.ExpiryDate <= request.ExpiresTo.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;

        var page = await query
            .OrderByDescending(x => x.ed.CreatedAt)
            .ThenBy(x => x.ed.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.ed.Id,
                x.ed.EmployeeId,
                x.d.Title,
                x.d.FileName,
                x.d.DocumentTypeId,
                DocumentTypeName = x.dt.Name,
                x.d.Status,
                x.ed.IssueDate,
                x.ed.ExpiryDate,
                IsAcknowledged = x.ed.AcknowledgedAt != null,
                x.ed.IsArchived,
                x.ed.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var employeeIds = page.Select(x => x.EmployeeId).Distinct().ToList();
        var employeeNames = employeeIds.Count > 0
            ? await employeeNameReader.GetNamesAsync(request.CompanyId, employeeIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var items = page
            .Select(x => new EmployeeDocumentSearchItem(
                x.Id,
                x.EmployeeId,
                employeeNames.TryGetValue(x.EmployeeId, out var name) ? name : x.EmployeeId.ToString(),
                x.Title,
                x.FileName,
                x.DocumentTypeId,
                x.DocumentTypeName,
                x.Status,
                x.IssueDate,
                x.ExpiryDate,
                x.IsAcknowledged,
                x.IsArchived,
                x.CreatedAt))
            .ToList();

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return Result.Success(new SearchEmployeeDocumentsResponse(items, totalCount, pageNumber, pageSize, totalPages));
    }
}
