using System.Globalization;
using System.Text.RegularExpressions;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

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
    Guid? PositionProfileId,
    Guid? ExistingEmployeeIdToUpdate = null)
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
    IImportLookupResolver lookupResolver,
    ICompanyEmployeeNumberSettingsReader employeeNumberSettingsReader)
{
    private static readonly string[] RequiredFields =
        ["FirstName", "LastName", "WorkEmail", "StartDate", "DateOfBirth", "Nationality", "Gender", "SalaryAmount"];

    // Lookup-by-name fields that resolve to a mandatory Employee foreign key (Department,
    // Location, EmploymentType, PositionProfile). These are validated for presence here in
    // addition to the ResolveLookupsAsync existence/auto-create logic below, since an employee
    // row missing any of them can never produce a valid Employee (all four are NOT NULL columns).
    private static readonly string[] RequiredLookupFields =
        ["DepartmentName", "LocationName", "EmploymentTypeName", "PositionProfileTitle"];
    private static readonly string[] DateFields = ["StartDate", "DateOfBirth", "ContinuousServiceDate", "ProbationEndDate"];
    // SalaryAmount itself is unconditionally mandatory (see RequiredFields) — an employee can never
    // have an opening compensation record with no salary figure, regardless of which other
    // compensation columns happen to be mapped for this import. It stays out of this array
    // (which only gates the fields that remain optional-if-mapped) but its numeric-format check
    // still lives in ValidateCompensationFields below, run unconditionally per row.
    private static readonly string[] CompensationFields = ["SalaryType", "Currency"];
    private static readonly string[] LeaveFields = ["LeaveBalanceDays"];
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
        var existingEmployeeIdToUpdateByRow = new Dictionary<int, Guid?>();

        var hasCompensationColumns = CompensationFields.Any(mappedFields.Contains);
        var hasLeaveColumns = LeaveFields.Any(mappedFields.Contains);
        var hasWorkingPatternColumns = WorkingPatternFields.Any(mappedFields.Contains);

        // Employee-number requiredness and duplicate-checking both depend on the company's
        // EmployeeNumberMode (Manual vs Automatic), read once up front for the whole batch.
        //
        // Automatic-mode rule (deliberate design decision for this feature, not an oversight):
        // a SUPPLIED employee number in the import file is REJECTED with a row-level validation
        // error rather than silently honoured or silently overwritten. Honouring an arbitrary
        // supplied number in automatic mode risks colliding with, or creating gaps in, the
        // atomic counter sequence (IEmployeeNumberGenerator); silently overwriting user-supplied
        // data is worse UX than a clear upfront error. Numbers are only ever generated for
        // automatic-mode rows once ALL rows in the batch have passed validation (at the later
        // ConfirmImportSession/commit step, never here at staging time) — this guarantees a
        // failed import never consumes/wastes employee numbers.
        var employeeNumberMode = await employeeNumberSettingsReader.GetModeAsync(companyId, cancellationToken);

        if (employeeNumberMode == EmployeeNumberMode.Manual)
            AddDuplicateErrors(rows, "EmployeeNumber", "employee number", errorsByRow);

        AddDuplicateErrors(rows, "WorkEmail", "work email", errorsByRow);

        foreach (var row in rows)
        {
            var rowErrors = errorsByRow[row.RowNumber];
            var rowWarnings = warningsByRow[row.RowNumber];

            ValidateRequiredFields(row, rowErrors);
            ValidateEmployeeNumberField(row, employeeNumberMode, rowErrors);
            ValidateDateFields(row, rowErrors);
            ValidateSalaryAmountFormat(row, rowErrors);

            Guid? existingEmployeeIdToUpdate;
            if (employeeNumberMode == EmployeeNumberMode.Manual)
                existingEmployeeIdToUpdate = await ValidateDuplicateAgainstExistingEmployeesAsync(companyId, row, rowErrors, cancellationToken);
            else
                existingEmployeeIdToUpdate = await ValidateWorkEmailAgainstExistingEmployeesAsync(companyId, row, rowErrors, cancellationToken);

            existingEmployeeIdToUpdateByRow[row.RowNumber] = existingEmployeeIdToUpdate;

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
                positionProfileIdByRow[r.RowNumber],
                existingEmployeeIdToUpdateByRow[r.RowNumber]))
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

        // Note: this only checks existence (read-only) — nothing is created here. Reference data
        // (new departments/locations/employment types/position profiles) is only ever created at
        // ConfirmImportSession time, once the user has reviewed the preview and clicked Confirm.
        // A row whose lookup doesn't yet exist keeps a null id here and surfaces as a "will be
        // created" warning; ConfirmImportSessionHandler re-resolves (get-or-create) each of these
        // fields by name immediately before creating the employee.
        var departmentName = GetField(row, "DepartmentName");
        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            departmentId = await lookupResolver.TryFindDepartmentAsync(companyId, departmentName, cancellationToken);
            if (departmentId is null)
                rowWarnings.Add($"Department '{departmentName.Trim()}' does not exist and will be created when this import is confirmed.");
        }

        var employmentTypeName = GetField(row, "EmploymentTypeName");
        if (!string.IsNullOrWhiteSpace(employmentTypeName))
        {
            employmentTypeId = await lookupResolver.TryFindEmploymentTypeAsync(companyId, employmentTypeName, cancellationToken);
            if (employmentTypeId is null)
                rowWarnings.Add($"Employment Type '{employmentTypeName.Trim()}' does not exist and will be created when this import is confirmed.");
        }

        var locationName = GetField(row, "LocationName");
        if (!string.IsNullOrWhiteSpace(locationName))
        {
            locationId = await lookupResolver.TryFindLocationAsync(companyId, locationName, cancellationToken);
            if (locationId is null)
                rowWarnings.Add($"Location '{locationName.Trim()}' does not exist and will be created when this import is confirmed.");
        }

        var positionProfileTitle = GetField(row, "PositionProfileTitle");
        if (!string.IsNullOrWhiteSpace(positionProfileTitle))
        {
            positionProfileId = await lookupResolver.TryFindPositionProfileAsync(companyId, positionProfileTitle, cancellationToken);

            if (positionProfileId is null)
            {
                // A brand-new position profile can only ever be created once Department and
                // Location are both present and resolvable on this row (mirrors
                // ImportLookupResolver.GetOrCreatePositionProfileAsync's own guard) — surfaced here
                // as an error (not a "will be created" warning) so the same row can never pass
                // validation only to fail unexpectedly at confirm time.
                if (string.IsNullOrWhiteSpace(departmentName) || string.IsNullOrWhiteSpace(locationName))
                {
                    rowErrors.Add(
                        $"Position Profile '{positionProfileTitle.Trim()}' could not be created because both Department and Location must be present and resolvable on this row.");
                }
                else
                {
                    rowWarnings.Add($"Position Profile '{positionProfileTitle.Trim()}' does not exist and will be created when this import is confirmed.");
                }
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

    // EmployeeNumberPattern lives on HR.Modules.Employees' CreateEmployeeValidator, which is
    // `internal` and therefore not visible from this module. Rather than reaching across a
    // module boundary or duplicating the literal, the pattern text is duplicated here with an
    // explicit comment pointing at the canonical definition, since no shared cross-module
    // validation-constants location currently exists in this codebase and introducing one for a
    // single regex is not justified.
    // Canonical definition: HR.Modules.Employees.Features.CreateEmployee.CreateEmployeeValidator.EmployeeNumberPattern
    private const string EmployeeNumberPattern = @"^[A-Za-z0-9 \-_./]+$";
    private static readonly Regex EmployeeNumberFormatRegex = new(EmployeeNumberPattern, RegexOptions.Compiled);

    private static void ValidateEmployeeNumberField(
        ParsedImportRow row, EmployeeNumberMode mode, List<string> rowErrors)
    {
        var employeeNumber = GetField(row, "EmployeeNumber");

        if (mode == EmployeeNumberMode.Automatic)
        {
            if (!string.IsNullOrWhiteSpace(employeeNumber))
            {
                rowErrors.Add(
                    "Employee number is auto-generated for this company and must be left blank.");
            }

            return;
        }

        // Manual mode: required. Enforced explicitly here (rather than via the static
        // RequiredFields array) because requiredness depends on the company's EmployeeNumberMode,
        // which is only known once this method has already read it.
        if (string.IsNullOrWhiteSpace(employeeNumber))
        {
            rowErrors.Add("'EmployeeNumber' is required.");
            return;
        }

        if (employeeNumber.Length > 50 || !EmployeeNumberFormatRegex.IsMatch(employeeNumber.Trim()))
        {
            rowErrors.Add(
                "Employee number may only contain letters, numbers, spaces, and the separators - _ . / (max 50 characters).");
        }
    }

    private async Task<Guid?> ValidateDuplicateAgainstExistingEmployeesAsync(
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

        return await ValidateWorkEmailAgainstExistingEmployeesAsync(companyId, row, rowErrors, cancellationToken);
    }

    // Returns the company's seed admin employee id when this row's Work Email matches it — see
    // Employee.IsInitialCompanyAdmin. That specific case is deliberately NOT a duplicate-email
    // error: it is the one and only situation where an employee import updates an existing
    // employee rather than creating a new one (see ConfirmImportSessionHandler). Every other
    // Work Email match against an existing employee remains a hard validation error, exactly as
    // before.
    private async Task<Guid?> ValidateWorkEmailAgainstExistingEmployeesAsync(
        Guid companyId,
        ParsedImportRow row,
        List<string> rowErrors,
        CancellationToken cancellationToken)
    {
        var workEmail = GetField(row, "WorkEmail");
        if (string.IsNullOrWhiteSpace(workEmail))
            return null;

        var seedAdminEmployeeId = await lookupReader.TryFindInitialCompanyAdminEmployeeIdByWorkEmailAsync(
            companyId, workEmail, cancellationToken);

        if (seedAdminEmployeeId is not null)
            return seedAdminEmployeeId;

        if (await lookupReader.WorkEmailExistsAsync(companyId, workEmail, cancellationToken))
            rowErrors.Add($"Work email '{workEmail}' already exists in this company.");

        return null;
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
             string.Equals(GetField(r, "WorkEmail")?.Trim(), normalizedReference, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 $"{GetField(r, "FirstName")?.Trim()} {GetField(r, "LastName")?.Trim()}",
                 normalizedReference,
                 StringComparison.OrdinalIgnoreCase)));

        if (matchesRowInFile)
            return;

        var matchedEmployeeId = await lookupReader.FindEmployeeIdByReferenceAsync(companyId, normalizedReference, cancellationToken);
        if (matchedEmployeeId is null)
            rowErrors.Add($"Manager reference '{managerReference}' does not match any employee in this file or company.");
    }

    // SalaryAmount's presence is enforced by ValidateRequiredFields (it's unconditionally
    // mandatory); this only checks the format of whatever value was supplied, and runs on every
    // row regardless of which other compensation columns were mapped.
    private static void ValidateSalaryAmountFormat(ParsedImportRow row, List<string> rowErrors)
    {
        var salaryAmount = GetField(row, "SalaryAmount");
        if (string.IsNullOrWhiteSpace(salaryAmount))
            return;

        if (!decimal.TryParse(salaryAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var salary) || salary <= 0)
            rowErrors.Add($"'SalaryAmount' value '{salaryAmount}' must be a positive number.");
    }

    private static void ValidateCompensationFields(ParsedImportRow row, List<string> rowErrors)
    {
        var salaryType = GetField(row, "SalaryType");
        if (!string.IsNullOrWhiteSpace(salaryType) && !ValidSalaryTypes.Contains(salaryType.Trim()))
            rowErrors.Add($"'SalaryType' value '{salaryType}' is not valid. Expected one of: {string.Join(", ", ValidSalaryTypes)}.");

        var currency = GetField(row, "Currency");
        if (!string.IsNullOrWhiteSpace(currency) && !CurrencyCodePattern.IsMatch(currency.Trim().ToUpperInvariant()))
            rowErrors.Add($"'Currency' value '{currency}' must be a 3-letter currency code.");
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
