using HR.Modules.Employees.Contracts;
namespace HR.Modules.DataImport.Services;

/// <summary>
/// Wraps a mapping of target field name -> expected source column header name.
/// Header lookups are case-insensitive.
/// </summary>
internal sealed class ColumnMappingProfile
{
    private readonly Dictionary<string, string> _targetToHeader;

    public ColumnMappingProfile(IReadOnlyDictionary<string, string> targetFieldToHeaderName)
    {
        _targetToHeader = new Dictionary<string, string>(targetFieldToHeaderName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> TargetFieldToHeaderName => _targetToHeader;

    /// <summary>
    /// Returns a new profile with the given target-field -> header-name overrides applied on
    /// top of this profile's mapping. Target fields not present in <paramref name="overrides"/>
    /// keep their existing header. A null or empty override collection returns this profile
    /// unchanged (as a new instance).
    /// </summary>
    public ColumnMappingProfile WithOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        var merged = new Dictionary<string, string>(_targetToHeader, StringComparer.OrdinalIgnoreCase);

        if (overrides is not null)
        {
            foreach (var (targetField, headerName) in overrides)
            {
                if (!string.IsNullOrWhiteSpace(headerName))
                    merged[targetField] = headerName;
            }
        }

        return new ColumnMappingProfile(merged);
    }
}

/// <summary>
/// The default column mapping for the standard employee import template.
/// </summary>
internal static class StandardEmployeeColumnMapping
{
    public static ColumnMappingProfile Default { get; } = new(new Dictionary<string, string>
    {
        ["FirstName"] = "First Name",
        ["LastName"] = "Last Name",
        ["PreferredName"] = "Preferred Name",
        ["WorkEmail"] = "Work Email",
        ["PersonalEmail"] = "Personal Email",
        ["StartDate"] = "Start Date",
        ["DateOfBirth"] = "Date Of Birth",
        ["Nationality"] = "Nationality",
        ["Gender"] = "Gender",
        ["EmployeeNumber"] = "Employee Number",
        ["ManagerReference"] = "Manager Reference",
        ["EmploymentTypeName"] = "Employment Type",
        ["DepartmentName"] = "Department",
        ["LocationName"] = "Location",
        ["PositionProfileTitle"] = "Position Profile",
        ["ContinuousServiceDate"] = "Continuous Service Date",
        ["ProbationEndDate"] = "Probation End Date",
        ["Address"] = "Address",
        ["SalaryAmount"] = "Salary Amount",
        ["SalaryType"] = "Salary Type",
        ["Currency"] = "Currency",
        // Hours Per Week and FTE are deliberately not import columns: they are always calculated
        // from Working Days + Hours Per Day (see WorkingPatternCompensationCalculator). Leave Type
        // Code was removed since the only leave type ever imported is Annual Leave (hardcoded in
        // ConfirmImportSessionHandler) — Leave Balance Days alone is enough to set its balance.
        ["LeaveBalanceDays"] = "Leave Balance Days",
        ["WorkingDays"] = "Working Days",
        ["HoursPerDay"] = "Hours Per Day",
    });
}
