using System.ComponentModel.DataAnnotations;

namespace HR.SharedKernel.Tests;

public class EnumTextTests
{
    public enum Sample
    {
        Trial,
        TrialExpired,
        PastDue,
        AdminForcedReadOnly,
        HalfDayAM,
        HalfDayPM,
        ExpiryReminder30,
        UKResident,
        [Display(Name = "Report a Problem")]
        ReportProblem,
        [Display(Name = "CV")]
        Cv,
        snake_case_value,
    }

    [Flags]
    public enum Days
    {
        None = 0,
        Monday = 1,
        Tuesday = 2,
        BankHoliday = 4,
    }

    [Theory]
    [InlineData(Sample.Trial, "Trial")]
    [InlineData(Sample.TrialExpired, "Trial Expired")]
    [InlineData(Sample.PastDue, "Past Due")]
    [InlineData(Sample.AdminForcedReadOnly, "Admin Forced Read Only")]
    [InlineData(Sample.HalfDayAM, "Half Day AM")]
    [InlineData(Sample.HalfDayPM, "Half Day PM")]
    [InlineData(Sample.ExpiryReminder30, "Expiry Reminder 30")]
    [InlineData(Sample.UKResident, "UK Resident")]
    [InlineData(Sample.snake_case_value, "Snake Case Value")]
    public void Humanize_Enum_Produces_Title_Case(Sample value, string expected)
    {
        Assert.Equal(expected, EnumText.Humanize(value));
        Assert.Equal(expected, value.ToDisplayText());
    }

    [Theory]
    [InlineData(Sample.ReportProblem, "Report a Problem")]
    [InlineData(Sample.Cv, "CV")]
    public void Humanize_Enum_Uses_Display_Attribute_Override(Sample value, string expected)
    {
        Assert.Equal(expected, EnumText.Humanize(value));
    }

    [Theory]
    [InlineData("TrialExpired", "Trial Expired")]
    [InlineData("NotStarted", "Not Started")]
    [InlineData("InProgress", "In Progress")]
    [InlineData("FormerEmployee", "Former Employee")]
    [InlineData("pending_provisioning", "Pending Provisioning")]
    [InlineData("employee-numbering.reformat-requested", "Employee Numbering Reformat Requested")]
    [InlineData("open", "Open")]
    [InlineData("ACTIVE", "ACTIVE")]
    [InlineData("HalfDayAM", "Half Day AM")]
    [InlineData("Not Started", "Not Started")]
    [InlineData("NoUser", "No User")]
    [InlineData("Hr", "HR")]
    [InlineData("Toil", "TOIL")]
    [InlineData("CandidateCv", "Candidate CV")]
    [InlineData("AsianOrAsianBritish", "Asian or Asian British")]
    [InlineData("BlackOrAfricanOrCaribbeanOrBlackBritish", "Black or African or Caribbean or Black British")]
    [InlineData("PreferNotToSay", "Prefer Not to Say")]
    public void Humanize_String_Splits_And_Cases(string value, string expected)
    {
        Assert.Equal(expected, EnumText.Humanize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Humanize_Blank_Returns_Empty(string? value)
    {
        Assert.Equal(string.Empty, EnumText.Humanize(value));
    }

    [Fact]
    public void Humanize_Null_Enum_Returns_Empty()
    {
        Assert.Equal(string.Empty, EnumText.Humanize((Enum?)null));
    }

    [Fact]
    public void Humanize_Unknown_Numeric_Enum_Value_Is_Left_As_Number()
    {
        Assert.Equal("42", EnumText.Humanize((Sample)42));
    }

    [Fact]
    public void Humanize_Flags_Combination_Is_Comma_Separated_Per_Member()
    {
        Assert.Equal("Monday, Tuesday", EnumText.Humanize(Days.Monday | Days.Tuesday));
        Assert.Equal("Monday, Bank Holiday", EnumText.Humanize(Days.Monday | Days.BankHoliday));
        Assert.Equal("None", EnumText.Humanize(Days.None));
    }

    [Fact]
    public void Humanize_Comma_Separated_String_Humanizes_Each_Part()
    {
        Assert.Equal("Monday, Bank Holiday", EnumText.Humanize("Monday, BankHoliday"));
    }
}
