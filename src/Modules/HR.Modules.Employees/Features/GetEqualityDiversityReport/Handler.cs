using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

/// <summary>
/// Builds anonymous, aggregated workforce equality statistics. Reads the encrypted equality
/// answers back through EF materialization (transparently decrypted in memory by the
/// <see cref="EmployeesDbContext"/> value converter) — there is no second unprotected copy.
/// Only counts and percentages leave this handler; small groups are collapsed into a
/// "Not reported" bucket so a single person can never be identified from the numbers.
/// </summary>
internal sealed class GetEqualityDiversityReportHandler(
    EmployeesDbContext db,
    IClock clock,
    IOptions<EqualityDiversityReportOptions> options)
{
    private const string NotStated = "Not stated";
    private const string NotReported = "Not reported";

    public async Task<Result<GetEqualityDiversityReportResponse>> HandleAsync(
        GetEqualityDiversityReportRequest request,
        CancellationToken cancellationToken)
    {
        var threshold = options.Value.ResolvedMinimumGroupSize;

        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId)
            .Select(e => new { e.Id, e.DateOfBirth })
            .ToListAsync(cancellationToken);

        var records = await db.EmployeeEqualityData
            .AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var total = employees.Count;
        var byEmployee = records
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var today = DateOnly.FromDateTime(clock.UtcNow);

        var dimensions = new List<EqualityReportDimension>
        {
            BuildEnumDimension("gender", "Gender", employees.Count, r => r.GenderIdentity),
            BuildDimension("age-band", "Age band", employees.Select(e => AgeBand(e.DateOfBirth, today))),
            BuildEnumDimension("ethnicity", "Ethnicity", employees.Count, r => r.EthnicGroup),
            BuildEnumDimension("disability", "Disability status", employees.Count, r => r.DisabilityStatus),
            BuildEnumDimension("sexual-orientation", "Sexual orientation", employees.Count, r => r.SexualOrientation),
            BuildEnumDimension("religion-or-belief", "Religion or belief", employees.Count, r => r.ReligionOrBelief),
            BuildEnumDimension("caring-responsibilities", "Caring responsibilities", employees.Count, r => r.CaringResponsibilities),
        };

        return Result.Success(new GetEqualityDiversityReportResponse(total, threshold, dimensions));

        EqualityReportDimension BuildEnumDimension(
            string key,
            string name,
            int employeeCount,
            Func<Domain.EmployeeEqualityData, string?> selector)
        {
            var answered = employees
                .Select(e => byEmployee.TryGetValue(e.Id, out var rec) ? Humanize(selector(rec)) : NotStated);
            return BuildDimensionInternal(key, name, answered, employeeCount);
        }

        EqualityReportDimension BuildDimension(string key, string name, IEnumerable<string> values)
        {
            var materialised = values.ToList();
            return BuildDimensionInternal(key, name, materialised, materialised.Count);
        }

        EqualityReportDimension BuildDimensionInternal(
            string key,
            string name,
            IEnumerable<string> values,
            int denominator)
        {
            var counts = values
                .GroupBy(v => v)
                .ToDictionary(g => g.Key, g => g.Count());

            var suppressedCount = 0;
            var visible = new List<KeyValuePair<string, int>>();
            foreach (var kvp in counts)
            {
                // "Not stated" / "Not reported" are already aggregate buckets — never suppress them.
                if (kvp.Value > 0 && kvp.Value < threshold && kvp.Key is not (NotStated or NotReported))
                    suppressedCount += kvp.Value;
                else
                    visible.Add(kvp);
            }

            // Secondary suppression: if the "Not reported" bucket itself is non-zero but still below
            // the threshold, fold the smallest visible real group into it so it cannot be inverted.
            while (suppressedCount > 0 && suppressedCount < threshold)
            {
                var smallest = visible
                    .Where(v => v.Key is not (NotStated or NotReported))
                    .OrderBy(v => v.Value)
                    .FirstOrDefault();
                if (smallest.Key is null)
                    break;
                visible.Remove(smallest);
                suppressedCount += smallest.Value;
            }

            var rows = visible
                .OrderByDescending(v => v.Value)
                .ThenBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => new EqualityReportRow(
                    v.Key,
                    v.Value,
                    Percentage(v.Value, denominator),
                    Suppressed: false))
                .ToList();

            if (suppressedCount > 0)
                rows.Add(new EqualityReportRow(NotReported, suppressedCount, Percentage(suppressedCount, denominator), Suppressed: true));

            return new EqualityReportDimension(key, name, rows);
        }
    }

    private static decimal Percentage(int count, int denominator)
        => denominator == 0 ? 0m : Math.Round(count * 100m / denominator, 1);

    private static string AgeBand(DateOnly dob, DateOnly today)
    {
        if (dob == default || dob > today)
            return "Unknown";

        var age = today.Year - dob.Year;
        if (dob > today.AddYears(-age))
            age--;

        return age switch
        {
            < 16 => "Unknown",
            <= 24 => "16-24",
            <= 34 => "25-34",
            <= 44 => "35-44",
            <= 54 => "45-54",
            <= 64 => "55-64",
            _ => "65+",
        };
    }

    private static string Humanize(string? enumMemberName)
    {
        if (string.IsNullOrWhiteSpace(enumMemberName))
            return NotStated;

        var chars = new List<char>(enumMemberName.Length + 4);
        for (var i = 0; i < enumMemberName.Length; i++)
        {
            var c = enumMemberName[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(enumMemberName[i - 1]))
                chars.Add(' ');
            chars.Add(c);
        }

        return new string(chars.ToArray());
    }
}
