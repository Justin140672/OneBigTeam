using HR.SharedKernel;

namespace HR.Web.Tests;

public class FormTextTests
{
    // ── Required ────────────────────────────────────────────────────────────

    [Fact]
    public void Required_Trims_Surrounding_Spaces()
    {
        Assert.Equal("hello", FormText.Required("  hello  "));
    }

    [Fact]
    public void Required_Preserves_Internal_Whitespace()
    {
        Assert.Equal("hello   world", FormText.Required("  hello   world  "));
    }

    [Fact]
    public void Required_Preserves_Unicode_Content()
    {
        Assert.Equal("café", FormText.Required("  café  "));
    }

    [Fact]
    public void Required_Does_Not_Collapse_Internal_Spaces_Or_Alter_Case()
    {
        Assert.Equal("Café   Noir", FormText.Required("  Café   Noir  "));
    }

    // ── Optional ────────────────────────────────────────────────────────────

    [Fact]
    public void Optional_Null_Returns_Null()
    {
        Assert.Null(FormText.Optional(null));
    }

    [Fact]
    public void Optional_Empty_String_Returns_Null()
    {
        Assert.Null(FormText.Optional(string.Empty));
    }

    [Fact]
    public void Optional_Whitespace_Only_Returns_Null()
    {
        Assert.Null(FormText.Optional("   "));
    }

    [Theory]
    [InlineData(" \t")]
    [InlineData("\n\n")]
    [InlineData("\r\n \t")]
    public void Optional_Various_Whitespace_Only_Inputs_Return_Null(string value)
    {
        Assert.Null(FormText.Optional(value));
    }

    [Fact]
    public void Optional_Non_Blank_String_Is_Trimmed()
    {
        Assert.Equal("hello", FormText.Optional("  hello  "));
    }

    [Fact]
    public void Optional_Preserves_Unicode_Content()
    {
        Assert.Equal("café", FormText.Optional("  café  "));
    }

    [Fact]
    public void Optional_Does_Not_Collapse_Internal_Spaces_Or_Alter_Case()
    {
        Assert.Equal("Café   Noir", FormText.Optional("  Café   Noir  "));
    }

    // ── OptionalSearch (delegates to Optional) ─────────────────────────────

    [Fact]
    public void OptionalSearch_Null_Returns_Null()
    {
        Assert.Null(FormText.OptionalSearch(null));
    }

    [Fact]
    public void OptionalSearch_Whitespace_Only_Returns_Null()
    {
        Assert.Null(FormText.OptionalSearch("   "));
    }

    [Fact]
    public void OptionalSearch_Non_Blank_String_Is_Trimmed()
    {
        Assert.Equal("search term", FormText.OptionalSearch("  search term  "));
    }
}
