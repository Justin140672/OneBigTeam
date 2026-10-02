using System.Text.RegularExpressions;

namespace HR.Web.Tests;

public class CompanyAdminAccessibilitySourceTests
{
    private static readonly string CompaniesRoot = Path.Combine("src", "HR.Web", "Components", "Pages", "Companies");

    private static string Read(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "src")) && dir.GetFiles("*.slnx").Concat(dir.GetFiles("*.sln")).Any()))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. relative]));
    }

    private static string ProfileTab => Read(CompaniesRoot, "CompanyProfileTab.razor");

    [Fact]
    public void Profile_Tab_Does_Not_Use_Bare_HrTextBox_Which_Emits_Generic_Textbox_Aria_Label()
    {
        Assert.DoesNotContain("<HrTextBox", ProfileTab);
    }

    [Theory]
    [InlineData("company-name", "Name", true)]
    [InlineData("addr-{i}-line1", "Address Line 1", true)]
    [InlineData("addr-{i}-line2", "Address Line 2", false)]
    [InlineData("addr-{i}-city", "Town/City", true)]
    [InlineData("addr-{i}-region", "County/Region", false)]
    [InlineData("addr-{i}-postcode", "Postcode", false)]
    public void Each_Field_Has_A_Label_And_Correct_Required_State(string id, string label, bool required)
    {
        var match = Regex.Match(ProfileTab, $@"<HrTextField Id=.{{0,6}}{Regex.Escape(id)}.*?/>", RegexOptions.Singleline);

        Assert.True(match.Success, $"No HrTextField for {id}");
        Assert.Contains($"Label=\"{label}\"", match.Value);
        Assert.Equal(required, match.Value.Contains("Required=\"true\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Address_Groups_Are_Fieldsets_With_A_Legend()
    {
        Assert.Contains("<fieldset", ProfileTab);
        Assert.Contains("<legend", ProfileTab);
        Assert.Contains("EnumDisplay.Humanize(addr.Type)", ProfileTab);
    }

    [Fact]
    public void Required_Flags_Match_Server_Validation()
    {
        var validator = Read("src", "Modules", "HR.Modules.Companies", "Features", "UpdateCompany", "Validator.cs");
        var model = Read("src", "HR.Web", "Models", "CompanyEditModels.cs");

        Assert.Matches(@"request\.Name\)\s*\.NotEmpty\(\)", validator);
        Assert.Matches(@"address\.Line1\)\s*\.NotEmpty\(\)", validator);
        Assert.Matches(@"address\.City\)\s*\.NotEmpty\(\)", validator);
        Assert.Matches(@"\[Required[^\]]*\]\s*(\[MaxLength\(\d+\)\]\s*)?public string\? Line1", model);
        Assert.Matches(@"\[Required[^\]]*\]\s*(\[MaxLength\(\d+\)\]\s*)?public string\? City", model);
    }

    [Fact]
    public void Cancel_Subscription_Uses_Destructive_Styling_And_Confirmation()
    {
        var source = Read(CompaniesRoot, "Subscription", "SubscriptionOverview.razor");

        Assert.Matches(@"<SfButton CssClass=""[^""]*e-danger[^""]*""[^>]*OnClick=""RequestCancelAsync""", source);
        Assert.DoesNotMatch(@"<SfButton[^>]*OnClick=""ConfirmCancelAsync""", source);
        Assert.Contains("<HrDestructiveConfirmDialog Visible=\"_showCancelConfirm\"", source);
        Assert.Contains("OnConfirmed=\"ConfirmCancelAsync\"", source);
        Assert.Matches(@"private void RequestCancelAsync\(\) => _showCancelConfirm = true;", source);
    }

    [Fact]
    public void Manage_Billing_Stays_Neutral()
    {
        var source = Read(CompaniesRoot, "Subscription", "SubscriptionOverview.razor");

        Assert.Matches(@"<SfButton Disabled=""@\(!ManageBillingIsAvailable", source);
    }

    [Fact]
    public void Company_Edit_Close_Does_Not_Resolve_Back_To_The_Same_Page()
    {
        var source = Read(CompaniesRoot, "CompanyEdit.razor");

        Assert.Contains("IsSamePage(null, Session.LandingUrl, $\"/companies/{Id}/edit\") ? \"/subscription\" : \"/\"", source);
        Assert.DoesNotContain("ListUrl => \"/\";", source);
    }
}
