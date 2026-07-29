namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Extended offboarding detail (status + last working day) for a single employee, as owned by
/// HR.Modules.Offboarding. Complements the existing single-field IOffboardingStatusReader — added
/// separately rather than widening that interface's return shape, to avoid touching its existing
/// callers.
/// </summary>
public interface IOffboardingDetailReader
{
    Task<OffboardingDetail?> GetDetailAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}

public sealed record OffboardingDetail(string Status, DateOnly LastWorkingDay);
