using HR.SharedKernel;

namespace HR.SharedKernel.Tests;

public class PersonNameTests
{
    [Fact]
    public void Display_Uses_Preferred_Name_When_Present()
    {
        Assert.Equal("Sarah Chen", PersonName.Display("Sara", "Chen", "Sarah"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Display_Falls_Back_To_Legal_First_Name_When_Preferred_Is_Blank(string? preferred)
    {
        Assert.Equal("Sara Chen", PersonName.Display("Sara", "Chen", preferred));
    }

    [Fact]
    public void Legal_Ignores_Preferred_Name()
    {
        Assert.Equal("Sara Chen", PersonName.Legal(" Sara ", " Chen "));
    }

    [Fact]
    public void DisplayWithLegal_Shows_Legal_Name_Only_When_Preferred_Differs()
    {
        Assert.Equal("Sarah Chen (legal name: Sara Chen)", PersonName.DisplayWithLegal("Sara", "Chen", "Sarah"));
        Assert.Equal("Sara Chen", PersonName.DisplayWithLegal("Sara", "Chen", "sara"));
        Assert.Equal("Sara Chen", PersonName.DisplayWithLegal("Sara", "Chen", null));
    }

    [Fact]
    public void IsPreferredDistinct_Is_False_For_Blank_Or_Same_Name()
    {
        Assert.False(PersonName.IsPreferredDistinct("Sara", null));
        Assert.False(PersonName.IsPreferredDistinct("Sara", "SARA"));
        Assert.True(PersonName.IsPreferredDistinct("Sara", "Sarah"));
    }

    [Fact]
    public void Initials_Use_Display_Name()
    {
        Assert.Equal("SC", PersonName.Initials("Sara", "Chen", null));
        Assert.Equal("BC", PersonName.Initials("Robert", "Chen", "Bob"));
    }
}
