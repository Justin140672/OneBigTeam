namespace HR.Modules.Companies.Features.ScheduleCustomerDeletion;

internal sealed record ScheduleCustomerDeletionRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Countdown length in days before the company becomes eligible for execution. Optional —
    /// defaults to <see cref="ScheduleCustomerDeletionHandler.DefaultCountdownDays"/> when omitted.
    /// </summary>
    public int? CountdownDays { get; init; }
}
