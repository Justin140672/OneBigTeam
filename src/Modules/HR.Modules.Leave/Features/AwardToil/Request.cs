namespace HR.Modules.Leave.Features.AwardToil;

internal sealed record AwardToilRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid AwardedByEmployeeId { get; init; }
    public decimal Days { get; init; }
    public DateOnly OccurredOn { get; init; }
    public string? Notes { get; init; }
}
