namespace HR.Web.Models;

public record ListLeaveTypesResponse(List<LeaveTypeListItemModel> Items);

public record LeaveTypeListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateLeaveTypeRequest(
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour);

public record CreateLeaveTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateLeaveTypeRequest(
    Guid CompanyId,
    Guid Id,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour);

public record UpdateLeaveTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed class LeaveTypeEditModel
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int DefaultEntitlementDays { get; set; }
    public string AccrualMethod { get; set; } = "None";
    public string Behaviour { get; set; } = "Standard";
}
