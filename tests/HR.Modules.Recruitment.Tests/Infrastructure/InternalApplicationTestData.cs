using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Internal recruitment Ticket 6: shared seeding helpers for the "identify internal applications"
/// tests. Application.Source == Internal is the ONLY authoritative internal indicator — a hired
/// external candidate also carries Candidate.EmployeeId (HireCandidate calls LinkToEmployee), so
/// these helpers make it easy to seed each of the shapes that must be told apart:
/// <list type="bullet">
/// <item>an internal application (employee-linked candidate + Source Internal),</item>
/// <item>an external application (unlinked candidate, any non-Internal or null Source),</item>
/// <item>a hired external application (candidate later linked to an employee, Source stays non-Internal).</item>
/// </list>
/// Entities are added to the context but not saved — callers SaveChangesAsync alongside their other data.
/// </summary>
internal static class InternalApplicationTestData
{
    public static (Candidate Candidate, Application Application) AddInternal(
        RecruitmentDbContext db,
        Guid companyId,
        Guid vacancyId,
        Guid stageId,
        Guid employeeId,
        DateTimeOffset now,
        string firstName = "Priya",
        string lastName = "Shah")
    {
        var candidate = Candidate.CreateForEmployee(
            Guid.NewGuid(), companyId, employeeId, firstName, lastName,
            $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@acme.example", null, now);
        var application = Application.Create(
            Guid.NewGuid(), companyId, vacancyId, candidate.Id, stageId, null, now, ApplicationSource.Internal);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        return (candidate, application);
    }

    public static (Candidate Candidate, Application Application) AddExternal(
        RecruitmentDbContext db,
        Guid companyId,
        Guid vacancyId,
        Guid stageId,
        ApplicationSource? source,
        DateTimeOffset now,
        string firstName = "Emma",
        string lastName = "Clarke")
    {
        var candidate = Candidate.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com", null, null, now);
        var application = Application.Create(
            Guid.NewGuid(), companyId, vacancyId, candidate.Id, stageId, null, now, source,
            source == ApplicationSource.ExternalRecruiter ? Guid.NewGuid() : null);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        return (candidate, application);
    }

    public static (Candidate Candidate, Application Application) AddHiredExternal(
        RecruitmentDbContext db,
        Guid companyId,
        Guid vacancyId,
        Guid hiredStageId,
        ApplicationSource? source,
        Guid employeeId,
        DateTimeOffset now,
        string firstName = "Liam",
        string lastName = "Turner")
    {
        var (candidate, application) = AddExternal(db, companyId, vacancyId, hiredStageId, source, now, firstName, lastName);
        candidate.LinkToEmployee(employeeId, now);
        return (candidate, application);
    }

    public static ApplicationSource? ParseSource(string? source) =>
        source is null ? null : Enum.Parse<ApplicationSource>(source);
}
