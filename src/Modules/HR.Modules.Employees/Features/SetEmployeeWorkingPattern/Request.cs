using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.SetEmployeeWorkingPattern;

internal sealed record SetEmployeeWorkingPatternRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
