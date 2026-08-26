using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;

namespace HR.Modules.Recruitment.Tests;

public class VacancyPositionProfileMatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    private static Vacancy CreateVacancy(Guid companyId, string? advertTitle) =>
        Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), advertTitle, null, Guid.NewGuid(), Now);

    [Fact]
    public async Task MatchAsync_Returns_Matched_When_Reader_Returns_Single_Candidate()
    {
        var candidateId = Guid.NewGuid();
        var reader = new FakePositionProfileReader(activeMatches: [candidateId]);
        var matcher = new VacancyPositionProfileMatcher(reader);
        var vacancy = CreateVacancy(Guid.NewGuid(), "Senior Software Engineer");

        var result = await matcher.MatchAsync(vacancy, CancellationToken.None);

        Assert.Equal(vacancy.Id, result.VacancyId);
        Assert.Equal(VacancyPositionProfileMatchOutcome.Matched, result.Outcome);
        Assert.Equal(candidateId, result.MatchedPositionProfileId);
        Assert.Equal([candidateId], result.CandidatePositionProfileIds);
    }

    [Fact]
    public async Task MatchAsync_Returns_Unmatched_When_Reader_Returns_No_Candidates()
    {
        var reader = new FakePositionProfileReader(); // defaults to an empty result -> no matches
        var matcher = new VacancyPositionProfileMatcher(reader);
        var vacancy = CreateVacancy(Guid.NewGuid(), "Senior Software Engineer");

        var result = await matcher.MatchAsync(vacancy, CancellationToken.None);

        Assert.Equal(vacancy.Id, result.VacancyId);
        Assert.Equal(VacancyPositionProfileMatchOutcome.Unmatched, result.Outcome);
        Assert.Null(result.MatchedPositionProfileId);
        Assert.Empty(result.CandidatePositionProfileIds);
    }

    [Fact]
    public async Task MatchAsync_Returns_Ambiguous_When_Reader_Returns_Multiple_Candidates()
    {
        var candidateIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var reader = new FakePositionProfileReader(activeMatches: candidateIds);
        var matcher = new VacancyPositionProfileMatcher(reader);
        var vacancy = CreateVacancy(Guid.NewGuid(), "Senior Software Engineer");

        var result = await matcher.MatchAsync(vacancy, CancellationToken.None);

        Assert.Equal(vacancy.Id, result.VacancyId);
        Assert.Equal(VacancyPositionProfileMatchOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.MatchedPositionProfileId);
        Assert.Equal(candidateIds, result.CandidatePositionProfileIds);
    }

    [Fact]
    public async Task MatchAsync_Passes_Vacancy_CompanyId_And_AdvertTitle_To_Reader()
    {
        var companyId = Guid.NewGuid();
        var reader = new RecordingPositionProfileReader();
        var matcher = new VacancyPositionProfileMatcher(reader);
        var vacancy = CreateVacancy(companyId, "Senior Software Engineer");

        await matcher.MatchAsync(vacancy, CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.Equal("Senior Software Engineer", reader.LastTitle);
    }

    [Fact]
    public async Task MatchAsync_Always_Passes_Null_DepartmentId_Since_Vacancy_Has_No_Department_Of_Its_Own()
    {
        // Judgment call (Refactor Duplicate Vacancy Fields): Vacancy.DepartmentId no longer exists, so
        // the matcher can only ever perform a company-wide, title-only match — see
        // VacancyPositionProfileMatcher's remarks.
        var companyId = Guid.NewGuid();
        var reader = new RecordingPositionProfileReader();
        var matcher = new VacancyPositionProfileMatcher(reader);
        var vacancy = CreateVacancy(companyId, "Product Designer");

        await matcher.MatchAsync(vacancy, CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.Null(reader.LastDepartmentId);
        Assert.Equal("Product Designer", reader.LastTitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MatchAsync_Returns_Unmatched_Without_Calling_Reader_When_AdvertTitle_Is_Null_Or_Whitespace(string? advertTitle)
    {
        var reader = new RecordingPositionProfileReader();
        var matcher = new VacancyPositionProfileMatcher(reader);
        var vacancy = CreateVacancy(Guid.NewGuid(), advertTitle);

        var result = await matcher.MatchAsync(vacancy, CancellationToken.None);

        Assert.Equal(VacancyPositionProfileMatchOutcome.Unmatched, result.Outcome);
        Assert.Null(result.MatchedPositionProfileId);
        Assert.Empty(result.CandidatePositionProfileIds);
        Assert.False(reader.WasCalled);
    }

    /// <summary>Records the arguments it was last called with, so tests can assert pass-through behavior.</summary>
    private sealed class RecordingPositionProfileReader : IPositionProfileReader
    {
        public bool WasCalled { get; private set; }
        public Guid? LastCompanyId { get; private set; }
        public Guid? LastDepartmentId { get; private set; }
        public string? LastTitle { get; private set; }

        public Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(
            Guid companyId, Guid? departmentId, string title, CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastCompanyId = companyId;
            LastDepartmentId = departmentId;
            LastTitle = title;
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }

        public Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            Task.FromResult<PositionProfileSummary?>(null);

        public Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(
            Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PositionProfileSummary>>([]);

        public Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(
            Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(
            Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            Task.FromResult<PositionProfileEmploymentDefaults?>(null);

        public Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
