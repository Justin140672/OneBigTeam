using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class VacancyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Vacancy CreateVacancy() =>
        Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Senior Software Engineer", "Description", Guid.NewGuid(), Now);

    [Fact]
    public void Create_Sets_Status_To_Draft()
    {
        var vacancy = CreateVacancy();

        Assert.Equal(VacancyStatus.Draft, vacancy.Status);
        Assert.Null(vacancy.OpenedAt);
        Assert.Null(vacancy.ClosedAt);
    }

    [Fact]
    public void Create_Sets_PositionProfileId()
    {
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(
            Guid.NewGuid(), Guid.NewGuid(), positionProfileId,
            "Senior Software Engineer", "Description", Guid.NewGuid(), Now);

        Assert.Equal(positionProfileId, vacancy.PositionProfileId);
    }

    [Fact]
    public void Create_PositionProfileId_Has_No_Nullable_Representation()
    {
        // PositionProfileId is a non-nullable Guid on both the Create() parameter and the domain
        // property, per product direction: "the only way to create a vacancy is from a position
        // profile, so it should be mandatory everywhere." There is no compiler-representable way
        // to construct a Vacancy with a missing/null PositionProfileId.
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(
            Guid.NewGuid(), Guid.NewGuid(), positionProfileId,
            "Senior Software Engineer", "Description", Guid.NewGuid(), Now);

        Guid nonNullablePositionProfileId = vacancy.PositionProfileId;
        Assert.Equal(positionProfileId, nonNullablePositionProfileId);
    }

    [Fact]
    public void Create_Trims_AdvertTitle_And_AdvertDescription()
    {
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "  Senior Software Engineer  ", "  Description  ", Guid.NewGuid(), Now);

        Assert.Equal("Senior Software Engineer", vacancy.AdvertTitle);
        Assert.Equal("Description", vacancy.AdvertDescription);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Accepts_Null_Or_Whitespace_AdvertTitle_Without_Throwing(string? advertTitle)
    {
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            advertTitle, "Description", Guid.NewGuid(), Now);

        Assert.Null(vacancy.AdvertTitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Accepts_Null_Or_Whitespace_AdvertDescription_Without_Throwing(string? advertDescription)
    {
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Senior Software Engineer", advertDescription, Guid.NewGuid(), Now);

        Assert.Null(vacancy.AdvertDescription);
    }

    [Fact]
    public void Open_From_Draft_Sets_Status_And_OpenedAt()
    {
        var vacancy = CreateVacancy();
        var openedAt = DateOnly.FromDateTime(Now.UtcDateTime);

        vacancy.Open(Now, openedAt);

        Assert.Equal(VacancyStatus.Open, vacancy.Status);
        Assert.Equal(openedAt, vacancy.OpenedAt);
    }

    [Fact]
    public void Open_When_Already_Closed_Throws()
    {
        var vacancy = CreateVacancy();
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        vacancy.Open(Now, date);
        vacancy.Close(Now, date);

        Assert.Throws<InvalidOperationException>(() => vacancy.Open(Now, date));
    }

    [Fact]
    public void Hold_From_Open_Sets_Status_To_OnHold()
    {
        var vacancy = CreateVacancy();
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        vacancy.Open(Now, date);

        vacancy.Hold(Now);

        Assert.Equal(VacancyStatus.OnHold, vacancy.Status);
    }

    [Fact]
    public void Open_From_OnHold_Sets_Status_To_Open()
    {
        // Open() accepts Draft *or* OnHold — this covers the OnHold branch of that condition,
        // which was previously only exercised via the Draft branch.
        var vacancy = CreateVacancy();
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        vacancy.Open(Now, date);
        vacancy.Hold(Now);

        vacancy.Open(Now, date);

        Assert.Equal(VacancyStatus.Open, vacancy.Status);
    }

    [Fact]
    public void Open_When_Cancelled_Throws()
    {
        var vacancy = CreateVacancy();
        vacancy.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime)));
    }

    [Fact]
    public void Hold_From_Draft_Throws()
    {
        var vacancy = CreateVacancy();

        Assert.Throws<InvalidOperationException>(() => vacancy.Hold(Now));
    }

    [Fact]
    public void Close_Sets_Status_And_ClosedAt()
    {
        var vacancy = CreateVacancy();
        var openedAt = DateOnly.FromDateTime(Now.UtcDateTime);
        var closedAt = openedAt.AddDays(30);
        vacancy.Open(Now, openedAt);

        vacancy.Close(Now, closedAt);

        Assert.Equal(VacancyStatus.Closed, vacancy.Status);
        Assert.Equal(closedAt, vacancy.ClosedAt);
    }

    [Fact]
    public void Cancel_When_Already_Cancelled_Throws()
    {
        var vacancy = CreateVacancy();
        vacancy.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => vacancy.Cancel(Now));
    }

    [Fact]
    public void Cancel_When_Closed_Throws()
    {
        // Cancel() rejects Closed *or* Cancelled — this covers the Closed branch of that
        // condition, which was previously only exercised via the Cancelled branch.
        var vacancy = CreateVacancy();
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        vacancy.Close(Now, date);

        Assert.Throws<InvalidOperationException>(() => vacancy.Cancel(Now));
    }

    [Fact]
    public void Cancel_From_Draft_Succeeds()
    {
        var vacancy = CreateVacancy();

        vacancy.Cancel(Now);

        Assert.Equal(VacancyStatus.Cancelled, vacancy.Status);
    }

    [Fact]
    public void Close_When_Already_Closed_Throws()
    {
        var vacancy = CreateVacancy();
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        vacancy.Close(Now, date);

        Assert.Throws<InvalidOperationException>(() => vacancy.Close(Now, date));
    }

    [Fact]
    public void Close_When_Cancelled_Throws()
    {
        // Close() rejects Closed *or* Cancelled — this covers the Cancelled branch of that
        // condition, which was previously only exercised via the Closed branch.
        var vacancy = CreateVacancy();
        vacancy.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime)));
    }

    [Fact]
    public void AssignPositionProfile_Sets_PositionProfileId_And_UpdatedAt()
    {
        var vacancy = CreateVacancy();
        var positionProfileId = Guid.NewGuid();
        var later = Now.AddDays(1);

        vacancy.AssignPositionProfile(positionProfileId, later);

        Assert.Equal(positionProfileId, vacancy.PositionProfileId);
        Assert.Equal(later, vacancy.UpdatedAt);
    }

    [Fact]
    public void AssignPositionProfile_Overwrites_Existing_PositionProfileId()
    {
        var vacancy = CreateVacancy();
        var originalPositionProfileId = vacancy.PositionProfileId;
        var newPositionProfileId = Guid.NewGuid();
        var later = Now.AddDays(1);

        vacancy.AssignPositionProfile(newPositionProfileId, later);

        Assert.NotEqual(originalPositionProfileId, vacancy.PositionProfileId);
        Assert.Equal(newPositionProfileId, vacancy.PositionProfileId);
    }

    [Fact]
    public void UpdateDetails_Trims_AdvertTitle_And_AdvertDescription()
    {
        var vacancy = CreateVacancy();
        var hiringManagerId = Guid.NewGuid();
        var later = Now.AddDays(1);

        vacancy.UpdateDetails("  New Title  ", "  New Description  ", hiringManagerId, null, later);

        Assert.Equal("New Title", vacancy.AdvertTitle);
        Assert.Equal("New Description", vacancy.AdvertDescription);
        Assert.Equal(hiringManagerId, vacancy.HiringManagerId);
        Assert.Equal(later, vacancy.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_Accepts_Null_Or_Whitespace_AdvertTitle_Without_Throwing(string? advertTitle)
    {
        var vacancy = CreateVacancy();

        vacancy.UpdateDetails(advertTitle, "Description", Guid.NewGuid(), null, Now.AddDays(1));

        Assert.Null(vacancy.AdvertTitle);
    }

    [Fact]
    public void UpdateDetails_Clears_Previously_Set_AdvertTitle_To_Null()
    {
        var vacancy = CreateVacancy();
        Assert.NotNull(vacancy.AdvertTitle);

        vacancy.UpdateDetails(null, vacancy.AdvertDescription, vacancy.HiringManagerId, null, Now.AddDays(1));

        Assert.Null(vacancy.AdvertTitle);
    }

    [Fact]
    public void Create_Sets_AssignedRecruiterId_When_Provided()
    {
        var recruiterId = Guid.NewGuid();

        var vacancy = Vacancy.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Senior Software Engineer", "Description", Guid.NewGuid(), Now, recruiterId);

        Assert.Equal(recruiterId, vacancy.AssignedRecruiterId);
    }

    [Fact]
    public void Create_Defaults_AssignedRecruiterId_To_Null()
    {
        var vacancy = CreateVacancy();

        Assert.Null(vacancy.AssignedRecruiterId);
    }

    [Fact]
    public void UpdateDetails_Sets_AssignedRecruiterId()
    {
        var vacancy = CreateVacancy();
        var recruiterId = Guid.NewGuid();

        vacancy.UpdateDetails(vacancy.AdvertTitle, vacancy.AdvertDescription, vacancy.HiringManagerId, recruiterId, Now.AddDays(1));

        Assert.Equal(recruiterId, vacancy.AssignedRecruiterId);
    }

    [Fact]
    public void UpdateDetails_Clears_AssignedRecruiterId_To_Null()
    {
        var vacancy = CreateVacancy();
        vacancy.UpdateDetails(vacancy.AdvertTitle, vacancy.AdvertDescription, vacancy.HiringManagerId, Guid.NewGuid(), Now.AddDays(1));

        vacancy.UpdateDetails(vacancy.AdvertTitle, vacancy.AdvertDescription, vacancy.HiringManagerId, null, Now.AddDays(2));

        Assert.Null(vacancy.AssignedRecruiterId);
    }

    [Fact]
    public void AssignRecruiter_Sets_AssignedRecruiterId_And_UpdatedAt()
    {
        var vacancy = CreateVacancy();
        var recruiterId = Guid.NewGuid();
        var later = Now.AddDays(1);

        vacancy.AssignRecruiter(recruiterId, later);

        Assert.Equal(recruiterId, vacancy.AssignedRecruiterId);
        Assert.Equal(later, vacancy.UpdatedAt);
    }

    [Fact]
    public void AssignRecruiter_Clears_AssignedRecruiterId_When_Passed_Null()
    {
        var vacancy = CreateVacancy();
        vacancy.AssignRecruiter(Guid.NewGuid(), Now.AddDays(1));

        vacancy.AssignRecruiter(null, Now.AddDays(2));

        Assert.Null(vacancy.AssignedRecruiterId);
    }
}
