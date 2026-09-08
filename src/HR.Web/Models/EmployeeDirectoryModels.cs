namespace HR.Web.Models;

// ── EMPLOYEE DIRECTORY (employee-facing) ──────────────────────────────────────

public record EmployeeDirectoryListResponse(
    List<EmployeeDirectoryListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public record EmployeeDirectoryListItem(
    Guid Id,
    string FirstName,
    string LastName,
    string? PreferredName,
    string? PositionTitle,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    string? WorkEmail,
    string? ProfilePhotoUrl)
{
    public string DisplayName =>
        $"{(string.IsNullOrWhiteSpace(PreferredName) ? FirstName : PreferredName)} {LastName}".Trim();

    public string Initials
    {
        get
        {
            var first = string.IsNullOrWhiteSpace(PreferredName) ? FirstName : PreferredName;
            var f = string.IsNullOrWhiteSpace(first) ? "" : first.Trim()[..1];
            var l = string.IsNullOrWhiteSpace(LastName) ? "" : LastName.Trim()[..1];
            var initials = $"{f}{l}".ToUpperInvariant();
            return string.IsNullOrEmpty(initials) ? "?" : initials;
        }
    }
}

public record EmployeeDirectoryDetail(
    Guid Id,
    string FirstName,
    string LastName,
    string? PreferredName,
    string? PositionTitle,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    string? WorkEmail,
    string? WorkPhone,
    DateOnly? StartDate,
    Guid? ManagerId,
    string? ManagerFullName,
    string? ProfilePhotoUrl)
{
    public string DisplayName =>
        $"{(string.IsNullOrWhiteSpace(PreferredName) ? FirstName : PreferredName)} {LastName}".Trim();

    public string Initials
    {
        get
        {
            var first = string.IsNullOrWhiteSpace(PreferredName) ? FirstName : PreferredName;
            var f = string.IsNullOrWhiteSpace(first) ? "" : first.Trim()[..1];
            var l = string.IsNullOrWhiteSpace(LastName) ? "" : LastName.Trim()[..1];
            var initials = $"{f}{l}".ToUpperInvariant();
            return string.IsNullOrEmpty(initials) ? "?" : initials;
        }
    }
}
