namespace HR.Modules.Leave.Domain;

internal sealed class HistoricalLeaveDeactivationRepairProgress
{
    private HistoricalLeaveDeactivationRepairProgress() { }

    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-00000000CAFE");

    public Guid Id { get; private set; }

    public DateTimeOffset? LastProcessedFinalisationCompletedAt { get; private set; }

    public Guid? LastProcessedEmployeeId { get; private set; }

    public bool IsComplete { get; private set; }

    public int TotalRepaired { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static HistoricalLeaveDeactivationRepairProgress CreateNew(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        IsComplete = false,
        TotalRepaired = 0,
        UpdatedAt = now,
    };

    public void Advance(DateTimeOffset lastFinalisationCompletedAt, Guid lastEmployeeId, int repairedInBatch, DateTimeOffset now)
    {
        LastProcessedFinalisationCompletedAt = lastFinalisationCompletedAt;
        LastProcessedEmployeeId = lastEmployeeId;
        TotalRepaired += repairedInBatch;
        UpdatedAt = now;
    }

    public void MarkComplete(DateTimeOffset now)
    {
        IsComplete = true;
        UpdatedAt = now;
    }
}
