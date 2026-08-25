using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.SaveReportView;

internal sealed class SaveReportViewHandler(ReportingDbContext dbContext, IClock clock)
{
    // Reserved for the built-in "Standard View" sentinel shown in the Saved Views dropdown
    // (ReportFilterPanel.razor) — a real saved view with this name would be indistinguishable
    // from it, so it can never be created or renamed to.
    internal const string ReservedStandardViewName = "Standard View";

    public async Task<Result<SaveReportViewResponse>> HandleAsync(
        SaveReportViewRequest request,
        Guid userId,
        ReportAccessGates accessGates,
        CancellationToken cancellationToken)
    {
        // REP-03: reject saved views for unknown report ids, reports the caller is not currently
        // authorized to view, or filter JSON that references fields/values the report doesn't
        // support — a saved view must never be persistable as a way to smuggle references to
        // reports/filters the caller cannot legitimately query.
        if (!ReportCatalog.TryGet(request.ReportId, out var definition))
            return Result.Failure<SaveReportViewResponse>(
                Error.Validation($"'{request.ReportId}' is not a recognised report."));

        if (!accessGates.IsAuthorized(definition.AccessGate))
            return Result.Failure<SaveReportViewResponse>(
                Error.Forbidden($"You do not have access to report '{request.ReportId}'."));

        var filterValidation = ReportFilterValidator.Validate(definition, request.FilterCriteriaJson);
        if (filterValidation.IsFailure)
            return Result.Failure<SaveReportViewResponse>(filterValidation.Error);

        var name = request.Name.Trim();

        if (string.Equals(name, ReservedStandardViewName, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<SaveReportViewResponse>(
                Error.Validation($"'{ReservedStandardViewName}' is a reserved name — please choose another."));

        var nameInUse = await dbContext.SavedReportViews
            .AnyAsync(
                v => v.CompanyId == request.CompanyId
                    && v.UserId == userId
                    && v.ReportId == request.ReportId
                    && v.Name.ToLower() == name.ToLower(),
                cancellationToken);

        if (nameInUse)
            return Result.Failure<SaveReportViewResponse>(
                Error.Conflict($"A saved view named '{name}' already exists."));

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
            name,
            request.FilterCriteriaJson,
            isDefault,
            clock.UtcNowOffset());

        dbContext.SavedReportViews.Add(view);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new SaveReportViewResponse(
            view.Id, view.ReportId, view.Name, view.FilterCriteriaJson, view.IsDefault, view.CreatedAt));
    }
}
