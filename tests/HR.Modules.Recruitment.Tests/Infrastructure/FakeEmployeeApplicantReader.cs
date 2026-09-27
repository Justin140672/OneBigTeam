using System.Collections.Concurrent;
using HR.Modules.Employees.Contracts;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Fake for <see cref="IEmployeeApplicantReader"/> (internal recruitment Ticket 4). Company-scoped
/// exactly like the real reader: a profile is only returned when both the company and the employee id
/// match, so "employee of another company" is indistinguishable from "unknown employee". Thread-safe so
/// it can be shared between concurrent handler calls in the PostgreSQL concurrency tests.
/// </summary>
internal sealed class FakeEmployeeApplicantReader : IEmployeeApplicantReader
{
    private readonly ConcurrentDictionary<(Guid CompanyId, Guid EmployeeId), EmployeeApplicantProfile> _profiles = new();

    public int Calls => _calls;
    private int _calls;

    public FakeEmployeeApplicantReader(params EmployeeApplicantProfile[] profiles)
    {
        foreach (var profile in profiles)
            Set(profile);
    }

    /// <summary>Adds or replaces the profile for (profile.CompanyId, profile.EmployeeId).</summary>
    public void Set(EmployeeApplicantProfile profile) =>
        _profiles[(profile.CompanyId, profile.EmployeeId)] = profile;

    public static EmployeeApplicantProfile Profile(
        Guid companyId,
        Guid employeeId,
        string firstName = "Priya",
        string lastName = "Shah",
        string workEmail = "priya.shah@acme.example",
        string? phoneNumber = "07700 900456",
        EmployeeApplicantEmploymentState state = EmployeeApplicantEmploymentState.Active) =>
        new(employeeId, companyId, firstName, lastName, workEmail, phoneNumber, state);

    public Task<EmployeeApplicantProfile?> GetApplicantAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(_profiles.TryGetValue((companyId, employeeId), out var profile) ? profile : null);
    }
}
