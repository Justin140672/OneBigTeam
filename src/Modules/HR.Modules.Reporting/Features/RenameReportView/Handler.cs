using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Features.SaveReportView;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.RenameReportView;

internal sealed class RenameReportViewHandler(ReportingDbContext dbContext)
{
    public async Task<Result<RenameReportViewResponse>> HandleAsync(
        RenameReportViewRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var view = await dbContext.SavedReportViews
            .FirstOrDefaultAsync(
                v => v.Id == request.ViewId && v.CompanyId == request.CompanyId && v.UserId == userId,
                cancellationToken);

        if (view is null)
            return Result.Failure<RenameReportViewResponse>(Error.NotFound("Saved report view not found."));

        var name = request.Name.Trim();

        if (string.Equals(name, SaveReportViewHandler.ReservedStandardViewName, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<RenameReportViewResponse>(
                Error.Validation($"'{SaveReportViewHandler.ReservedStandardViewName}' is a reserved name — please choose another."));

        // Excludes the view's own current row so renaming to its own existing name (a no-op) isn't
        // flagged as a collision with itself.
        var nameInUse = await dbContext.SavedReportViews
            .AnyAsync(
                v => v.Id != view.Id
                    && v.CompanyId == request.CompanyId
                    && v.UserId == userId
                    && v.ReportId == view.ReportId
                    && v.Name.ToLower() == name.ToLower(),
                cancellationToken);

        if (nameInUse)
            return Result.Failure<RenameReportViewResponse>(
                Error.Conflict($"A saved view named '{name}' already exists."));

        view.Rename(name);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new RenameReportViewResponse(view.Id, view.Name));
    }
}
