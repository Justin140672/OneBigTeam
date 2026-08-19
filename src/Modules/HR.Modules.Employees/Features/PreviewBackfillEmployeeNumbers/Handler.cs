using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.PreviewBackfillEmployeeNumbers;

internal sealed class PreviewBackfillEmployeeNumbersHandler(
    EmployeesDbContext dbContext,
    ICompanyEmployeeNumberSettingsReader employeeNumberSettingsReader)
{
    public async Task<Result<PreviewBackfillEmployeeNumbersResponse>> HandleAsync(
        PreviewBackfillEmployeeNumbersRequest request,
        CancellationToken cancellationToken)
    {
        var mode = await employeeNumberSettingsReader.GetModeAsync(request.CompanyId, cancellationToken);

        if (mode != EmployeeNumberMode.Automatic)
        {
            return Result.Failure<PreviewBackfillEmployeeNumbersResponse>(
                Error.Conflict(
                    "Employee number backfill is only available when the company's employee-numbering mode is Automatic."));
        }

        // Empty string is the only representation of "no employee number" in this codebase (the
        // column is NOT NULL) — never null.
        var candidates = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId && e.EmployeeNumber == "")
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.StartDate })
            .ToListAsync(cancellationToken);

        var sequencePreview = await employeeNumberSettingsReader.GetSequencePreviewAsync(request.CompanyId, cancellationToken);

        var results = new List<BackfillCandidatePreview>(candidates.Count);
        var nextNumber = sequencePreview.NextNumber;

        foreach (var candidate in candidates)
        {
            var predictedNumber =
                $"{sequencePreview.Prefix}{nextNumber.ToString().PadLeft(sequencePreview.MinimumLength, '0')}";

            results.Add(new BackfillCandidatePreview(
                candidate.Id, candidate.FirstName, candidate.LastName, candidate.StartDate, predictedNumber));

            nextNumber++;
        }

        return Result.Success(new PreviewBackfillEmployeeNumbersResponse(results));
    }
}
