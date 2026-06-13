using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

public class PublicHolidayTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_All_Properties()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var date = new DateOnly(2026, 12, 25);

        var holiday = PublicHoliday.Create(id, companyId, date, "Christmas Day", "GB", Now);

        Assert.Equal(id, holiday.Id);
        Assert.Equal(companyId, holiday.CompanyId);
        Assert.Equal(date, holiday.Date);
        Assert.Equal("Christmas Day", holiday.Name);
        Assert.Equal("GB", holiday.CountryCode);
        Assert.Equal(Now, holiday.CreatedAt);
    }
}
