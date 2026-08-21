namespace HR.Modules.Employees.Features.ListPositionProfiles;

internal sealed record ListPositionProfilesRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeInactive { get; init; } = false;

    // Both optional/backward-compatible — omitted entirely by every existing caller (the
    // PositionProfileList.razor admin grid, document-audience pickers, report filters, etc. all
    // need the complete company set for their own client-side paging/filtering), so the handler's
    // behavior for them is unchanged. Added so a caller that only ever needs a bounded,
    // type-to-search dropdown (see EmployeeEmploymentTab.razor's Position Profile field) doesn't
    // have to pull every Position Profile the company has ever had — see PageSize's remarks on the
    // handler for why an unbounded fetch here became a real, user-observed multi-second delay.
    public string? Search { get; init; }
    public int? PageSize { get; init; }
}
