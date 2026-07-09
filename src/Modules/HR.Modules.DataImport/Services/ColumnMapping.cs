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
        ["SalaryAmount"] = "Salary Amount",
        ["SalaryType"] = "Salary Type",
        ["Currency"] = "Currency",
        ["HoursPerWeek"] = "Hours Per Week",
        ["FTE"] = "FTE",
        ["LeaveTypeCode"] = "Leave Type Code",
        ["LeaveBalanceDays"] = "Leave Balance Days",
        ["WorkingDays"] = "Working Days",
        ["HoursPerDay"] = "Hours Per Day",
    });
}
