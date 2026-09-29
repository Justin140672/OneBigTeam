using System.Collections.Concurrent;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Internal recruitment Ticket 7: fake for <see cref="IEmployeeInternalAppointmentService"/>. Models the
/// real service's contract closely enough for Recruitment-side tests:
/// <list type="bullet">
/// <item>idempotent on (CompanyId, SourceReference) — a repeated AppointAsync returns the change already
/// recorded with WasAlreadyRecorded = true and records nothing new;</item>
/// <item>can be scripted to fail (nothing is recorded, exactly like the real service's failure contract);</item>
/// <item>supports <see cref="ResumeBySourceReferenceAsync"/> and <see cref="Seed"/> for recovery tests;</item>
/// <item>thread-safe, so it can be shared between concurrent handler calls in PostgreSQL tests.</item>
/// </list>
/// </summary>
internal sealed class FakeEmployeeInternalAppointmentService : IEmployeeInternalAppointmentService
{
    private readonly ConcurrentDictionary<(Guid CompanyId, string SourceReference), InternalAppointmentResult> _recorded = new();
    private readonly ConcurrentQueue<InternalAppointmentRequest> _appointRequests = new();
    private readonly ConcurrentQueue<(Guid CompanyId, string SourceReference, Guid PerformedBy)> _resumeCalls = new();
    private readonly object _gate = new();

    public Error? FailWith { get; set; }

    public DateOnly? Today { get; set; }

    public Guid NewDepartmentId { get; set; } = Guid.NewGuid();
    public Guid NewLocationId { get; set; } = Guid.NewGuid();
    public Guid PreviousPositionProfileId { get; set; } = Guid.NewGuid();

    public Func<Task>? BeforeAppoint { get; set; }

    public IReadOnlyList<InternalAppointmentRequest> AppointRequests => _appointRequests.ToList();
    public IReadOnlyList<(Guid CompanyId, string SourceReference, Guid PerformedBy)> ResumeCalls => _resumeCalls.ToList();

    public int RecordedCount => _recorded.Count;

    public InternalAppointmentResult Seed(
        Guid companyId,
        string sourceReference,
        Guid employeeId,
        Guid newPositionProfileId,
        DateOnly effectiveDate,
        Guid? newManagerId = null,
        bool isApplied = true,
        Guid? promotionId = null)
    {
        var result = new InternalAppointmentResult(
            promotionId ?? Guid.NewGuid(),
            employeeId,
            PreviousPositionProfileId,
            newPositionProfileId,
            NewDepartmentId,
            NewLocationId,
            newManagerId,
            effectiveDate,
            CompensationId: null,
            IsApplied: isApplied,
            WasAlreadyRecorded: false);

        _recorded[(companyId, sourceReference)] = result;
        return result;
    }

    public async Task<Result<InternalAppointmentResult>> AppointAsync(
        InternalAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        _appointRequests.Enqueue(request);

        if (BeforeAppoint is not null)
            await BeforeAppoint();

        if (FailWith is { } error)
            return Result.Failure<InternalAppointmentResult>(error);

        lock (_gate)
        {
            var key = (request.CompanyId, request.SourceReference);
            if (_recorded.TryGetValue(key, out var existing))
                return Result.Success(existing with { WasAlreadyRecorded = true });

            var created = new InternalAppointmentResult(
                Guid.NewGuid(),
                request.EmployeeId,
                PreviousPositionProfileId,
                request.NewPositionProfileId,
                NewDepartmentId,
                NewLocationId,
                request.NewManagerId,
                request.EffectiveDate,
                request.Compensation is null ? null : Guid.NewGuid(),
                IsApplied: Today is not { } today || request.EffectiveDate <= today,
                WasAlreadyRecorded: false);

            _recorded[key] = created;
            return Result.Success(created);
        }
    }

    public Task<InternalAppointmentResult?> ResumeBySourceReferenceAsync(
        Guid companyId,
        string sourceReference,
        Guid performedByUserId,
        CancellationToken cancellationToken)
    {
        _resumeCalls.Enqueue((companyId, sourceReference, performedByUserId));

        return Task.FromResult(_recorded.TryGetValue((companyId, sourceReference), out var recorded)
            ? recorded with { WasAlreadyRecorded = true }
            : null);
    }
}
