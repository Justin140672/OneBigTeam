using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Tests;

public class WorkEmailAddressBuilderTests
{
    [Theory]
    [InlineData(WorkEmailNamingConvention.FirstNameDotLastName, "jane.smith")]
    [InlineData(WorkEmailNamingConvention.FirstInitialDotLastName, "j.smith")]
    [InlineData(WorkEmailNamingConvention.FirstNameLastName, "janesmith")]
    [InlineData(WorkEmailNamingConvention.FirstName, "jane")]
    public void BuildLocalPart_Applies_Each_Naming_Convention(WorkEmailNamingConvention convention, string expected)
    {
        Assert.Equal(expected, WorkEmailAddressBuilder.BuildLocalPart(convention, "Jane", "Smith"));
    }

    [Fact]
    public void BuildLocalPart_Returns_Null_For_Undefined_Convention()
    {
        Assert.Null(WorkEmailAddressBuilder.BuildLocalPart((WorkEmailNamingConvention)99, "Jane", "Smith"));
    }

    [Theory]
    [InlineData(null, "Smith")]
    [InlineData("", "Smith")]
    [InlineData("   ", "Smith")]
    [InlineData("Jane", null)]
    [InlineData("Jane", "")]
    [InlineData("Jane", "   ")]
    [InlineData("'-", "Smith")]
    [InlineData("Jane", "'-")]
    public void BuildLocalPart_Returns_Null_When_First_Or_Last_Name_Is_Missing(string? first, string? last)
    {
        foreach (var convention in Enum.GetValues<WorkEmailNamingConvention>())
            Assert.Null(WorkEmailAddressBuilder.BuildLocalPart(convention, first, last));
    }

    [Theory]
    [InlineData("O'Brien", "obrien")]
    [InlineData("Anne-Marie", "annemarie")]
    [InlineData("Mary Jane", "maryjane")]
    [InlineData("  Jane  ", "jane")]
    [InlineData("JANE", "jane")]
    [InlineData("José", "jose")]
    [InlineData("Zoë", "zoe")]
    [InlineData("Müller", "muller")]
    [InlineData("Strauß", "strauss")]
    [InlineData("Søren", "soren")]
    [InlineData("Æther", "aether")]
    [InlineData("Łukasz", "lukasz")]
    [InlineData("Đorđe", "dorde")]
    [InlineData("Jane2", "jane2")]
    [InlineData("J.R.", "jr")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeNamePart_Strips_Diacritics_And_Punctuation(string? input, string expected)
    {
        Assert.Equal(expected, WorkEmailAddressBuilder.NormalizeNamePart(input));
    }

    [Fact]
    public void BuildLocalPart_Uses_First_Letter_Of_Normalised_First_Name_For_Initial_Convention()
    {
        Assert.Equal("e.obrien", WorkEmailAddressBuilder.BuildLocalPart(
            WorkEmailNamingConvention.FirstInitialDotLastName, "Éloïse", "O'Brien"));
    }

    [Fact]
    public void BuildLocalPart_Returns_Null_When_Result_Exceeds_Maximum_Length()
    {
        var first = new string('a', 33);
        var last = new string('b', 31);
        Assert.Null(WorkEmailAddressBuilder.BuildLocalPart(
            WorkEmailNamingConvention.FirstNameDotLastName, first, last));
    }

    [Fact]
    public void BuildLocalPart_Accepts_Result_At_Maximum_Length()
    {
        var first = new string('a', 32);
        var last = new string('b', 31);
        var localPart = WorkEmailAddressBuilder.BuildLocalPart(
            WorkEmailNamingConvention.FirstNameDotLastName, first, last);

        Assert.Equal(WorkEmailAddressBuilder.MaxLocalPartLength, localPart!.Length);
    }

    [Theory]
    [InlineData("example.co.uk", "example.co.uk")]
    [InlineData("@example.co.uk", "example.co.uk")]
    [InlineData("  @Example.CO.uk  ", "example.co.uk")]
    [InlineData("EXAMPLE.COM", "example.com")]
    [InlineData("@", null)]
    [InlineData("@  ", null)]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeDomain_Trims_Lowercases_And_Strips_Leading_At(string? input, string? expected)
    {
        Assert.Equal(expected, WorkEmailAddressBuilder.NormalizeDomain(input));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("example.co.uk")]
    [InlineData("sub.example.com")]
    [InlineData("my-company.io")]
    [InlineData("a1.example.org")]
    public void IsValidDomain_Accepts_Valid_Domains(string domain)
    {
        Assert.True(WorkEmailAddressBuilder.IsValidDomain(domain));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("example")]
    [InlineData("example.")]
    [InlineData(".example.com")]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    [InlineData("exa mple.com")]
    [InlineData("exa_mple.com")]
    [InlineData("example.c")]
    [InlineData("example.123")]
    [InlineData("user@example.com")]
    [InlineData("example..com")]
    [InlineData("Example.com")]
    public void IsValidDomain_Rejects_Invalid_Domains(string? domain)
    {
        Assert.False(WorkEmailAddressBuilder.IsValidDomain(domain));
    }

    [Fact]
    public void IsValidDomain_Rejects_Domain_Longer_Than_Maximum()
    {
        var domain = string.Join('.', Enumerable.Repeat(new string('a', 60), 5)) + ".com";
        Assert.True(domain.Length > WorkEmailAddressBuilder.MaxDomainLength);
        Assert.False(WorkEmailAddressBuilder.IsValidDomain(domain));
    }

    [Fact]
    public void BuildAddress_Combines_Local_Part_And_Domain()
    {
        Assert.Equal("jane.smith@example.co.uk", WorkEmailAddressBuilder.BuildAddress(
            WorkEmailNamingConvention.FirstNameDotLastName, "Jane", "Smith", "example.co.uk"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a domain")]
    public void BuildAddress_Returns_Null_When_Domain_Is_Invalid(string? domain)
    {
        Assert.Null(WorkEmailAddressBuilder.BuildAddress(
            WorkEmailNamingConvention.FirstNameDotLastName, "Jane", "Smith", domain));
    }

    [Theory]
    [InlineData("jane@Acme.Example", "acme.example")]
    [InlineData("  jane@acme.co.uk ", "acme.co.uk")]
    [InlineData("jane@xn--80ak6aa92e.com", "xn--80ak6aa92e.com")]
    [InlineData("jane@sub.acme.xn--p1ai", "sub.acme.xn--p1ai")]
    [InlineData("jane@localhost", null)]
    [InlineData("jane@@acme.com", null)]
    [InlineData("a@b@acme.com", null)]
    [InlineData("@acme.com", null)]
    [InlineData("jane", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractDomain_Returns_Normalised_Valid_Domain_Or_Null(string? email, string? expected)
    {
        Assert.Equal(expected, WorkEmailAddressBuilder.ExtractDomain(email));
    }

    [Theory]
    [InlineData("jane.smith@acme.com", "Jane", "Smith", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData("Jane.Smith@acme.com", "Jane", "Smith", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData("j.smith@acme.com", "Jane", "Smith", WorkEmailNamingConvention.FirstInitialDotLastName)]
    [InlineData("janesmith@acme.com", "Jane", "Smith", WorkEmailNamingConvention.FirstNameLastName)]
    [InlineData("jane@acme.com", "Jane", "Smith", WorkEmailNamingConvention.FirstName)]
    [InlineData("anne-marie.obrien@acme.com", "Anne-Marie", "O'Brien", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData("admin@acme.com", "Jane", "Smith", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData("jsmith@acme.com", "Jane", "Smith", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData("jane.smith@acme.com", "", "", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData("nonsense", "Jane", "Smith", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData(null, "Jane", "Smith", WorkEmailNamingConvention.FirstNameDotLastName)]
    public void InferNamingConvention_Matches_The_Local_Part_Or_Defaults_To_FirstNameDotLastName(
        string? email, string firstName, string lastName, WorkEmailNamingConvention expected)
    {
        Assert.Equal(expected, WorkEmailAddressBuilder.InferNamingConvention(email, firstName, lastName));
    }

    [Fact]
    public void BuildAddress_Returns_Null_When_Name_Is_Incomplete()
    {
        Assert.Null(WorkEmailAddressBuilder.BuildAddress(
            WorkEmailNamingConvention.FirstNameDotLastName, "Jane", null, "example.com"));
    }
}
