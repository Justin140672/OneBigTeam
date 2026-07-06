using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListCandidateDocuments;

internal sealed class ListCandidateDocumentsHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListCandidateDocumentsResponse>> HandleAsync(
        ListCandidateDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await db.CandidateDocuments
            .AsNoTracking()
            .Where(cd => cd.CompanyId == request.CompanyId && cd.CandidateId == request.CandidateId)
            .OrderByDescending(cd => cd.CreatedAt)
            .Select(cd => new CandidateDocumentListItem(
                cd.Id,
                cd.Title,
                cd.FileName,
                cd.FileSize,
                cd.ContentType,
                cd.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListCandidateDocumentsResponse(items));
    }
}
