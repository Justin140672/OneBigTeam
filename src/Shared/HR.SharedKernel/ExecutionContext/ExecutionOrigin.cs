namespace HR.SharedKernel.ExecutionContext;

/// <summary>
/// Ticket 23: classifies what kind of work is currently executing, so logs, audit records and
/// durable operations can distinguish an interactive HTTP request from asynchronous work that
/// outlives it.
/// </summary>
public enum ExecutionOrigin
{
    HttpRequest = 0,
    IntegrationEvent = 1,
    ScheduledJob = 2,
    ReconciliationJob = 3,
    ManualRetry = 4,
}
