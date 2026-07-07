using System.Globalization;
using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Services;

/// <summary>
/// The outcome of validating a single staged employee import row.
/// </summary>
internal sealed record RowValidationResult(int RowNumber, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates parsed employee import rows for a single import session: required fields,
/// duplicate employee numbers/work emails (within the file and against existing employees),
/// date fields, manager references, and (only when the relevant columns are mapped)
/// compensation and leave balance fields.
/// </summary>
internal sealed class EmployeeStagingRowValidator(IEmployeeImportLookupReader lookupReader)
{
    private static readonly string[] RequiredFields = ["FirstName", "LastName", "WorkEmail", "StartDate"];
    private static readonly string[] DateFields = ["StartDate", "DateOfBirth", "ContinuousServiceDate", "ProbationEndDate"];
    private static readonly string[] CompensationFields = ["SalaryAmount", "SalaryType", "Currency", "HoursPerWeek", "FTE"];
    private static readonly string[] LeaveFields = ["LeaveTypeCode", "LeaveBalanceDays"];

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

        var hasCompensationColumns = CompensationFields.Any(mappedFields.Contains);
        var hasLeaveColumns = LeaveFields.Any(mappedFields.Contains);

        AddDuplicateErrors(rows, "EmployeeNumber", "employee number", errorsByRow);
        AddDuplicateErrors(rows, "WorkEmail", "work email", errorsByRow);

        foreach (var row in rows)
        {
            var rowErrors = errorsByRow[row.RowNumber];

            ValidateRequiredFields(row, rowErrors);
            ValidateDateFields(row, rowErrors);
            await ValidateDuplicateAgainstExistingEmployeesAsync(companyId, row, rowErrors, cancellationToken);
            await ValidateManagerReferenceAsync(companyId, row, rows, rowErrors, cancellationToken);

            if (hasCompensationColumns)
                ValidateCompensationFields(row, rowErrors);

            if (hasLeaveColumns)
                ValidateLeaveFields(row, rowErrors);
        }

        return rows
            .Select(r => new RowValidationResult(r.RowNumber, errorsByRow[r.RowNumber]))
            .ToList();
    }

    private static void ValidateRequiredFields(ParsedImportRow row, List<string> rowErrors)
    {
        foreach (var field in RequiredFields)
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
