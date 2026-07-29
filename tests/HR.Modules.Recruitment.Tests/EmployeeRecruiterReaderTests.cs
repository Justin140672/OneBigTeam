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
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", $"alice{Guid.NewGuid():N}@example.com", null, null, Now);
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
}
