using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class EmployeeRecruiterReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class FakeEmployeeNameReader(IReadOnlyDictionary<Guid, string> names) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken) =>
            Task.FromResult(names);
    }

    private static (Candidate candidate, Application application, Vacancy vacancy) SeedHire(
        RecruitmentDbContext db,
        Guid companyId,
        Guid employeeId,
        Guid hiringManagerId,
        Guid? assignedRecruiterId,
        Guid stageId)
    {
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", $"alice{Guid.NewGuid():N}@example.com", null, Now);
        candidate.LinkToEmployee(employeeId, Now);

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, hiringManagerId, Now, assignedRecruiterId);

        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stageId, null, Now);

        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(application);

        return (candidate, application, vacancy);
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Returns_Empty_When_No_EmployeeIds_Supplied()
    {
        await using var db = BuildContext();
        var reader = new EmployeeRecruiterReader(db, new FakeEmployeeNameReader(new Dictionary<Guid, string>()));

        var result = await reader.GetRecruiterNamesAsync(Guid.NewGuid(), [], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Uses_ExternalRecruiter_AgencyName_When_Assigned()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var externalRecruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(externalRecruiter);

        SeedHire(db, companyId, employeeId, Guid.NewGuid(), externalRecruiter.Id, Guid.NewGuid());
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(db, new FakeEmployeeNameReader(new Dictionary<Guid, string>()));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.Equal("Acme Recruiting", result[employeeId]);
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Falls_Back_To_HiringManager_When_No_ExternalRecruiter_Assigned()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        SeedHire(db, companyId, employeeId, hiringManagerId, assignedRecruiterId: null, Guid.NewGuid());
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(
            db, new FakeEmployeeNameReader(new Dictionary<Guid, string> { [hiringManagerId] = "Bob Jones" }));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.Equal("Bob Jones (Hiring Manager)", result[employeeId]);
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Omits_Employee_When_HiringManager_Name_Not_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        SeedHire(db, companyId, employeeId, hiringManagerId, assignedRecruiterId: null, Guid.NewGuid());
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(db, new FakeEmployeeNameReader(new Dictionary<Guid, string>()));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.False(result.ContainsKey(employeeId));
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Omits_Employee_Not_Linked_To_Any_Candidate_Hire()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new EmployeeRecruiterReader(db, new FakeEmployeeNameReader(new Dictionary<Guid, string>()));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.Empty(result);
        Assert.False(result.ContainsKey(employeeId));
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        SeedHire(db, otherCompanyId, employeeId, hiringManagerId, assignedRecruiterId: null, Guid.NewGuid());
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(
            db, new FakeEmployeeNameReader(new Dictionary<Guid, string> { [hiringManagerId] = "Bob Jones" }));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.Empty(result);
    }

    // ----- Internal recruitment Ticket 4 -----

    [Fact]
    public async Task GetRecruiterNamesAsync_Ignores_Later_Internal_Application_By_The_Hired_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var originalRecruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var internalRecruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Other Agency", null, null, null, null, null, Now);
        db.ExternalRecruiters.AddRange(originalRecruiter, internalRecruiter);

        var (candidate, _, _) = SeedHire(db, companyId, employeeId, Guid.NewGuid(), originalRecruiter.Id, Guid.NewGuid());

        // The employee later applies internally (reusing the linked candidate). Its AppliedAt is
        // deliberately dated BEFORE the hire application so that an "earliest application" rule that
        // did not exclude Internal would pick it.
        var internalVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Lead Engineer", null, Guid.NewGuid(), Now, internalRecruiter.Id);
        db.Vacancies.Add(internalVacancy);
        db.Applications.Add(Application.Create(
            Guid.NewGuid(), companyId, internalVacancy.Id, candidate.Id, Guid.NewGuid(), null, Now.AddDays(-1), ApplicationSource.Internal));
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(db, new FakeEmployeeNameReader(new Dictionary<Guid, string>()));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.Equal("Acme Recruiting", result[employeeId]);
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Omits_Employee_Whose_Only_Applications_Are_Internal()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        var candidate = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, hiringManagerId, Now);
        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(Application.Create(
            Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now, ApplicationSource.Internal));
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(
            db, new FakeEmployeeNameReader(new Dictionary<Guid, string> { [hiringManagerId] = "Bob Jones" }));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.False(result.ContainsKey(employeeId));
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Picks_Earliest_NonInternal_Application_Deterministically()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var earlierRecruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Earlier Agency", null, null, null, null, null, Now);
        var laterRecruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Later Agency", null, null, null, null, null, Now);
        db.ExternalRecruiters.AddRange(earlierRecruiter, laterRecruiter);

        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice.twice@example.com", null, Now);
        candidate.LinkToEmployee(employeeId, Now);
        var laterVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Later", null, Guid.NewGuid(), Now, laterRecruiter.Id);
        var earlierVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Earlier", null, Guid.NewGuid(), Now, earlierRecruiter.Id);
        db.Candidates.Add(candidate);
        db.Vacancies.AddRange(laterVacancy, earlierVacancy);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, laterVacancy.Id, candidate.Id, Guid.NewGuid(), null, Now.AddDays(-1), ApplicationSource.Direct));
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, earlierVacancy.Id, candidate.Id, Guid.NewGuid(), null, Now.AddDays(-30)));
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(db, new FakeEmployeeNameReader(new Dictionary<Guid, string>()));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.Equal("Earlier Agency", result[employeeId]);
    }

    [Fact]
    public async Task GetRecruiterNamesAsync_Omits_Employee_Whose_Only_Application_Is_A_Completed_Internal_Appointment()
    {
        // Internal recruitment Ticket 7: an internal appointment moves the application to the Hired
        // stage, but the employee was not "recruited" by that vacancy — "recruited by" is about how the
        // employee originally joined, so an appointed internal application must not create an entry.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        var externalRecruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(externalRecruiter);

        var candidate = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", "priya.appointed@acme.example", null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineering Manager", null, hiringManagerId, Now, externalRecruiter.Id);
        var application = Application.Create(
            Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now, ApplicationSource.Internal);
        application.BeginInternalAppointment(employeeId, Guid.NewGuid(), Now);
        application.CompleteInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 1), Now);
        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var reader = new EmployeeRecruiterReader(
            db, new FakeEmployeeNameReader(new Dictionary<Guid, string> { [hiringManagerId] = "Bob Jones" }));

        var result = await reader.GetRecruiterNamesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.False(result.ContainsKey(employeeId));
    }
}
