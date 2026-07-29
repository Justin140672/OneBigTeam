using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.SaveReportView;

internal sealed class SaveReportViewHandler(ReportingDbContext dbContext, IClock clock)
{
    public async Task<Result<SaveReportViewResponse>> HandleAsync(
        SaveReportViewRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var isDefault = request.IsDefault ?? false;

        if (isDefault)
        {
            var existingDefaults = await dbContext.SavedReportViews
                .Where(v => v.CompanyId == request.CompanyId
                    && v.UserId == userId
                    && v.ReportId == request.ReportId
                    && v.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var existingDefault in existingDefaults)
                existingDefault.SetIsDefault(false);
        }

        var view = SavedReportView.Create(
            Guid.NewGuid(),
            request.CompanyId,
            userId,
            request.ReportId,
            request.Name,
            request.FilterCriteriaJson,
            isDefault,
            clock.UtcNowOffset());

        dbContext.SavedReportViews.Add(view);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new SaveReportViewResponse(
            view.Id, view.ReportId, view.Name, view.FilterCriteriaJson, view.IsDefault, view.CreatedAt));
    }
}
