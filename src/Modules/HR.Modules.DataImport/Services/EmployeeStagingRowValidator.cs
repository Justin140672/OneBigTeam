using System.Globalization;
using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Services;

/// <summary>
/// The outcome of validating a single staged employee import row.
/// </summary>
internal sealed record RowValidationResult(
    int RowNumber,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? EmploymentTypeId,
    Guid? PositionProfileId)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates parsed employee import rows for a single import session: required fields,
/// duplicate employee numbers/work emails (within the file and against existing employees),
/// date fields, manager references, and (only when the relevant columns are mapped)
/// compensation and leave balance fields. Also resolves Department/EmploymentType/Location/
/// PositionProfile references by name, auto-creating any that do not already exist for the
/// company (recorded as Warning-severity row messages).
/// </summary>
internal sealed class EmployeeStagingRowValidator(
    IEmployeeImportLookupReader lookupReader,
    IImportLookupResolver lookupResolver)
{
    private static readonly string[] RequiredFields =
        ["FirstName", "LastName", "WorkEmail", "StartDate", "DateOfBirth", "Nationality", "Gender", "EmployeeNumber"];

    // Lookup-by-name fields that resolve to a mandatory Employee foreign key (Department,
    // Location, EmploymentType, PositionProfile). These are validated for presence here in
    // addition to the ResolveLookupsAsync existence/auto-create logic below, since an employee
    // row missing any of them can never produce a valid Employee (all four are NOT NULL columns).
    private static readonly string[] RequiredLookupFields =
        ["DepartmentName", "LocationName", "EmploymentTypeName", "PositionProfileTitle"];
    private static readonly string[] DateFields = ["StartDate", "DateOfBirth", "ContinuousServiceDate", "ProbationEndDate"];
    private static readonly string[] CompensationFields = ["SalaryAmount", "SalaryType", "Currency", "HoursPerWeek", "FTE"];
    private static readonly string[] LeaveFields = ["LeaveTypeCode", "LeaveBalanceDays"];
    private static readonly string[] WorkingPatternFields = ["WorkingDays", "HoursPerDay"];

    // Comma-separated day names, e.g. "Monday,Tuesday,Wednesday,Thursday,Friday" — mirrors the
    // flags on HR.Infrastructure.Abstractions.WorkingDays.
    private static readonly HashSet<string> ValidDayNames =
        new(StringComparer.OrdinalIgnoreCase) { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    // Mirrors the SalaryType values accepted by HR.Modules.Employees' Domain.SalaryType enum
    // (Annual, Hourly, Daily) via CreateCompensationRecord's validator (IsInEnum()).
    private static readonly HashSet<string> ValidSalaryTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Annual", "Hourly", "Daily" };

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy"];
    private static readonly Regex CurrencyCodePattern = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<RowValidationResult>> ValidateAsync(
        Guid companyId,
        IReadOnlyList<ParsedImportRow> rows,
        IReadOnlySet<string> mappedFields,
        CancellationToken cancellationToken)
    {
        var errorsByRow = rows.ToDictionary(r => r.RowNumber, _ => new List<string>());
        var warningsByRow = rows.ToDictionary(r => r.RowNumber, _ => new List<string>());
        var departmentIdByRow = new Dictionary<int, Guid?>();
        var locationIdByRow = new Dictionary<int, Guid?>();
        var employmentTypeIdByRow = new Dictionary<int, Guid?>();
        var positionProfileIdByRow = new Dictionary<int, Guid?>();

        var hasCompensationColumns = CompensationFields.Any(mappedFields.Contains);
        var hasLeaveColumns = LeaveFields.Any(mappedFields.Contains);
        var hasWorkingPatternColumns = WorkingPatternFields.Any(mappedFields.Contains);

        AddDuplicateErrors(rows, "EmployeeNumber", "employee number", errorsByRow);
        AddDuplicateErrors(rows, "WorkEmail", "work email", errorsByRow);

        foreach (var row in rows)
        {
            var rowErrors = errorsByRow[row.RowNumber];
            var rowWarnings = warningsByRow[row.RowNumber];

            ValidateRequiredFields(row, rowErrors);
            ValidateDateFields(row, rowErrors);
            await ValidateDuplicateAgainstExistingEmployeesAsync(companyId, row, rowErrors, cancellationToken);
            await ValidateManagerReferenceAsync(companyId, row, rows, rowErrors, cancellationToken);

            if (hasCompensationColumns)
                ValidateCompensationFields(row, rowErrors);

            if (hasLeaveColumns)
                ValidateLeaveFields(row, rowErrors);

            if (hasWorkingPatternColumns)
                ValidateWorkingPatternFields(row, rowErrors);

            var (departmentId, locationId, employmentTypeId, positionProfileId) =
                await ResolveLookupsAsync(companyId, row, rowErrors, rowWarnings, cancellationToken);

            departmentIdByRow[row.RowNumber] = departmentId;
            locationIdByRow[row.RowNumber] = locationId;
            employmentTypeIdByRow[row.RowNumber] = employmentTypeId;
            positionProfileIdByRow[row.RowNumber] = positionProfileId;
        }

        return rows
            .Select(r => new RowValidationResult(
                r.RowNumber,
                errorsByRow[r.RowNumber],
                warningsByRow[r.RowNumber],
                departmentIdByRow[r.RowNumber],
                locationIdByRow[r.RowNumber],
                employmentTypeIdByRow[r.RowNumber],
                positionProfileIdByRow[r.RowNumber]))
            .ToList();
    }

    private async Task<(Guid? DepartmentId, Guid? LocationId, Guid? EmploymentTypeId, Guid? PositionProfileId)> ResolveLookupsAsync(
        Guid companyId,
        ParsedImportRow row,
        List<string> rowErrors,
        List<string> rowWarnings,
        CancellationToken cancellationToken)
    {
        Guid? departmentId = null;
        Guid? locationId = null;
        Guid? employmentTypeId = null;
        Guid? positionProfileId = null;

        var departmentName = GetField(row, "DepartmentName");
        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            var result = await lookupResolver.GetOrCreateDepartmentAsync(companyId, departmentName, cancellationToken);
            departmentId = result.Id;
            if (result.WasCreated)
                rowWarnings.Add($"Department '{departmentName.Trim()}' did not exist and was created.");
        }

        var employmentTypeName = GetField(row, "EmploymentTypeName");
        if (!string.IsNullOrWhiteSpace(employmentTypeName))
        {
            var result = await lookupResolver.GetOrCreateEmploymentTypeAsync(companyId, employmentTypeName, cancellationToken);
            employmentTypeId = result.Id;
            if (result.WasCreated)
                rowWarnings.Add($"Employment Type '{employmentTypeName.Trim()}' did not exist and was created.");
        }

        var locationName = GetField(row, "LocationName");
        if (!string.IsNullOrWhiteSpace(locationName))
        {
            var result = await lookupResolver.GetOrCreateLocationAsync(companyId, locationName, cancellationToken);
            locationId = result.Id;
            if (result.WasCreated)
                rowWarnings.Add($"Location '{locationName.Trim()}' did not exist and was created.");
        }

        var positionProfileTitle = GetField(row, "PositionProfileTitle");
        if (!string.IsNullOrWhiteSpace(positionProfileTitle))
        {
            var result = await lookupResolver.GetOrCreatePositionProfileAsync(
                companyId, positionProfileTitle, departmentId, locationId, cancellationToken);

            if (result.Skipped)
            {
                rowErrors.Add(
                    $"Position Profile '{positionProfileTitle.Trim()}' could not be created because both Department and Location must be present and resolvable on this row.");
            }
            else
            {
                positionProfileId = result.Id;
                if (result.WasCreated)
                    rowWarnings.Add($"Position Profile '{positionProfileTitle.Trim()}' did not exist and was created.");
            }
        }

        return (departmentId, locationId, employmentTypeId, positionProfileId);
    }

    private static void ValidateRequiredFields(ParsedImportRow row, List<string> rowErrors)
    {
        foreach (var field in RequiredFields)
        {
            if (string.IsNullOrWhiteSpace(GetField(row, field)))
                rowErrors.Add($"'{field}' is required.");
        }

        foreach (var field in RequiredLookupFields)
        {
            if (string.IsNullOrWhiteSpace(GetField(row, field)))
                rowErrors.Add($"'{field}' is required.");
        }
    }

    private static void ValidateDateFields(ParsedImportRow row, List<string> rowErrors)
    {
        foreach (var field in DateFields)
        {
            var value = GetField(row, field);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!TryParseDate(value, out var parsed))
            {
                rowErrors.Add($"'{field}' value '{value}' is not a valid date.");
                continue;
            }

            if (field == "DateOfBirth" && parsed >= DateOnly.FromDateTime(DateTime.UtcNow))
                rowErrors.Add("'DateOfBirth' must be in the past.");
        }
    }

    private async Task ValidateDuplicateAgainstExistingEmployeesAsync(
        Guid companyId,
        ParsedImportRow row,
        List<string> rowErrors,
        CancellationToken cancellationToken)
    {
        var employeeNumber = GetField(row, "EmployeeNumber");
        if (!string.IsNullOrWhiteSpace(employeeNumber) &&
            await lookupReader.EmployeeNumberExistsAsync(companyId, employeeNumber, cancellationToken))
        {
            rowErrors.Add($"Employee number '{employeeNumber}' already exists in this company.");
        }

        var workEmail = GetField(row, "WorkEmail");
        if (!string.IsNullOrWhiteSpace(workEmail) &&
            await lookupReader.WorkEmailExistsAsync(companyId, workEmail, cancellationToken))
        {
            rowErrors.Add($"Work email '{workEmail}' already exists in this company.");
        }
    }

    private async Task ValidateManagerReferenceAsync(
        Guid companyId,
        ParsedImportRow row,
        IReadOnlyList<ParsedImportRow> allRows,
        List<string> rowErrors,
        CancellationToken cancellationToken)
    {
        var managerReference = GetField(row, "ManagerReference");
        if (string.IsNullOrWhiteSpace(managerReference))
            return;

        var normalizedReference = managerReference.Trim();

        var matchesRowInFile = allRows.Any(r =>
            r.RowNumber != row.RowNumber &&
            (string.Equals(GetField(r, "EmployeeNumber")?.Trim(), normalizedReference, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetField(r, "WorkEmail")?.Trim(), normalizedReference, StringComparison.OrdinalIgnoreCase)));

        if (matchesRowInFile)
            return;

        var matchedEmployeeId = await lookupReader.FindEmployeeIdByReferenceAsync(companyId, normalizedReference, cancellationToken);
        if (matchedEmployeeId is null)
            rowErrors.Add($"Manager reference '{managerReference}' does not match any employee in this file or company.");
    }

    private static void ValidateCompensationFields(ParsedImportRow row, List<string> rowErrors)
    {
        var salaryAmount = GetField(row, "SalaryAmount");
        if (!string.IsNullOrWhiteSpace(salaryAmount))
        {
            if (!decimal.TryParse(salaryAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var salary) || salary <= 0)
                rowErrors.Add($"'SalaryAmount' value '{salaryAmount}' must be a positive number.");
        }

        var salaryType = GetField(row, "SalaryType");
        if (!string.IsNullOrWhiteSpace(salaryType) && !ValidSalaryTypes.Contains(salaryType.Trim()))
            rowErrors.Add($"'SalaryType' value '{salaryType}' is not valid. Expected one of: {string.Join(", ", ValidSalaryTypes)}.");

        var currency = GetField(row, "Currency");
        if (!string.IsNullOrWhiteSpace(currency) && !CurrencyCodePattern.IsMatch(currency.Trim().ToUpperInvariant()))
            rowErrors.Add($"'Currency' value '{currency}' must be a 3-letter currency code.");

        var hoursPerWeek = GetField(row, "HoursPerWeek");
        if (!string.IsNullOrWhiteSpace(hoursPerWeek))
        {
            if (!decimal.TryParse(hoursPerWeek, NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) || hours <= 0)
                rowErrors.Add($"'HoursPerWeek' value '{hoursPerWeek}' must be a positive number.");
        }

        var fte = GetField(row, "FTE");
        if (!string.IsNullOrWhiteSpace(fte))
        {
            if (!decimal.TryParse(fte, NumberStyles.Number, CultureInfo.InvariantCulture, out var fteValue) || fteValue < 0 || fteValue > 1)
                rowErrors.Add($"'FTE' value '{fte}' must be between 0 and 1.");
        }
    }

    private static void ValidateWorkingPatternFields(ParsedImportRow row, List<string> rowErrors)
    {
        var workingDays = GetField(row, "WorkingDays");
        if (!string.IsNullOrWhiteSpace(workingDays))
        {
            var dayNames = workingDays.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (dayNames.Length == 0)
            {
                rowErrors.Add("'WorkingDays' must contain at least one day name.");
            }
            else
            {
                var invalid = dayNames.Where(d => !ValidDayNames.Contains(d)).ToList();
                if (invalid.Count > 0)
                    rowErrors.Add($"'WorkingDays' contains invalid day name(s): {string.Join(", ", invalid)}. Expected comma-separated day names, e.g. 'Monday,Tuesday,Wednesday,Thursday,Friday'.");
            }
        }

        var hoursPerDay = GetField(row, "HoursPerDay");
        if (!string.IsNullOrWhiteSpace(hoursPerDay))
        {
            if (!decimal.TryParse(hoursPerDay, NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) || hours <= 0)
                rowErrors.Add($"'HoursPerDay' value '{hoursPerDay}' must be a positive number.");
        }
    }

    private static void ValidateLeaveFields(ParsedImportRow row, List<string> rowErrors)
    {
        var leaveBalanceDays = GetField(row, "LeaveBalanceDays");
        if (!string.IsNullOrWhiteSpace(leaveBalanceDays))
        {
            if (!decimal.TryParse(leaveBalanceDays, NumberStyles.Number, CultureInfo.InvariantCulture, out var days) || days < 0)
                rowErrors.Add($"'LeaveBalanceDays' value '{leaveBalanceDays}' must be a non-negative number.");
        }

        // LeaveTypeCode is format-only checked (non-empty when present); the parser already
        // normalizes empty/whitespace cell values to null, so a present value is guaranteed non-empty.
    }

    private static void AddDuplicateErrors(
        IReadOnlyList<ParsedImportRow> rows,
        string field,
        string fieldDescription,
        Dictionary<int, List<string>> errorsByRow)
    {
        var groups = rows
            .Where(r => !string.IsNullOrWhiteSpace(GetField(r, field)))
            .GroupBy(r => GetField(r, field)!.Trim().ToLowerInvariant());

        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            foreach (var row in group)
            {
                errorsByRow[row.RowNumber].Add(
                    $"Duplicate {fieldDescription} '{GetField(row, field)}' appears in multiple rows of this file.");
            }
        }
    }

    private static string? GetField(ParsedImportRow row, string field) =>
        row.Fields.TryGetValue(field, out var value) ? value : null;

    private static bool TryParseDate(string value, out DateOnly result)
    {
        var trimmed = value.Trim();

        if (DateOnly.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
