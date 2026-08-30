using HR.Infrastructure.Email;

namespace HR.Infrastructure.Tests.Email;

public class PostmarkPasswordResetEmailModelTests
{
    private static EmailBrandingOptions Branding() => new()
    {
        ProductName = "One Big Team",
        CompanyName = "One Big Team Ltd",
        CompanyAddress = "1 Test Street, London",
        SupportUrl = "https://onebigteam.co.uk/support",
        SupportEmail = "help@onebigteam.co.uk",
    };

    [Fact]
    public void BuildTemplateModel_Populates_Every_Required_Field()
    {
        var model = PostmarkPasswordResetEmailSender.BuildTemplateModel(
            Branding(),
            productUrl: "https://app.onebigteam.co.uk",
            recipientName: "Ada Lovelace",
            actionUrl: "https://proj.supabase.co/auth/v1/verify?token=abc&type=recovery",
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        Assert.Equal("https://app.onebigteam.co.uk", model["product_url"]);
        Assert.Equal("One Big Team", model["product_name"]);
        Assert.Equal("Ada Lovelace", model["name"]);
        Assert.Equal("https://proj.supabase.co/auth/v1/verify?token=abc&type=recovery", model["action_url"]);
        Assert.Equal("Windows", model["operating_system"]);
        Assert.Equal("Chrome", model["browser_name"]);
        Assert.Equal("https://onebigteam.co.uk/support", model["support_url"]);
        Assert.Equal("One Big Team Ltd", model["company_name"]);
        Assert.Equal("1 Test Street, London", model["company_address"]);

        // Exactly the nine fields the Postmark template contract defines.
        Assert.Equal(9, model.Count);
    }

    [Fact]
    public void BuildTemplateModel_Uses_Empty_Strings_For_Missing_Optional_Values()
    {
        var branding = new EmailBrandingOptions { ProductName = "One Big Team", CompanyName = "One Big Team" };

        var model = PostmarkPasswordResetEmailSender.BuildTemplateModel(
            branding,
            productUrl: "https://app.local",
            recipientName: null,
            actionUrl: "https://link",
            userAgent: null);

        Assert.Equal(string.Empty, model["name"]);
        Assert.Equal(string.Empty, model["support_url"]);
        Assert.Equal(string.Empty, model["company_address"]);
        Assert.Equal("Unknown", model["operating_system"]);
        Assert.Equal("Unknown", model["browser_name"]);
    }
}
