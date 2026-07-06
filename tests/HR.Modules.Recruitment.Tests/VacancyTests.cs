using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class VacancyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Vacancy CreateVacancy() =>
        Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Senior Software Engineer", "Description", "Remote", Guid.NewGuid(), Now);

    [Fact]
    public void Create_Sets_Status_To_Draft()
    {
        var vacancy = CreateVacancy();

        Assert.Equal(VacancyStatus.Draft, vacancy.Status);
        Assert.Null(vacancy.OpenedAt);
        Assert.Null(vacancy.ClosedAt);
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
}
