using System.ComponentModel.DataAnnotations;

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
    bool HasBalance,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateLeaveTypeRequest(
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool HasBalance = true);

public record CreateLeaveTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    bool HasBalance,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateLeaveTypeRequest(
    Guid CompanyId,
    Guid Id,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool HasBalance = true);

public record UpdateLeaveTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    bool HasBalance,
    bool IsSystem,
    DateTimeOffset UpdatedAt);

public sealed class LeaveTypeEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "Code is required.")]
    public string Code { get; set; } = string.Empty;
    [Range(0, int.MaxValue, ErrorMessage = "Default days cannot be negative.")]
    public int DefaultEntitlementDays { get; set; }
    public string AccrualMethod { get; set; } = "None";
    public string Behaviour { get; set; } = "Standard";
    public bool HasBalance { get; set; } = true;

    /// <summary>
    /// True for the platform-provisioned "Annual Leave" record — see LeaveType.IsSystem on the
    /// backend. Read-only here: there is no way to set this via Create, and it drives the
    /// Name-field-disabled / no-delete UI in LeaveTypeEdit.razor / LeaveTypeList.razor.
    /// </summary>
    public bool IsSystem { get; set; }
}
