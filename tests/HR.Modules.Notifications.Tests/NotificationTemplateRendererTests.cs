using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests;

// NOT-03: unit tests for the deterministic "{TokenName}" substitution engine. Wording assertions for
// the shipped catalogue templates are pinned here (via NotificationTemplateCatalogue.All, not
// hand-rolled strings) so a future accidental wording change in the catalogue fails this test rather
// than only being caught downstream.
public class NotificationTemplateRendererTests
{
    [Fact]
    public void Render_LeaveApproved_With_All_Required_Tokens_Produces_Expected_Wording()
    {
        var template = NotificationTemplateCatalogue.All[NotificationType.LeaveApproved];
        var tokens = new Dictionary<string, string>
        {
            ["StartDate"] = "3 Aug 2026",
            ["EndDate"] = "7 Aug 2026",
        };

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsSuccess);
        var rendered = result.Value!;
        Assert.Equal("Your leave request has been approved", rendered.InAppTitle);
        Assert.Equal("Your leave from 3 Aug 2026 to 7 Aug 2026 has been approved.", rendered.InAppBody);
        Assert.Equal("Your leave request has been approved", rendered.EmailSubject);
        Assert.Contains("Your leave from 3 Aug 2026 to 7 Aug 2026 has been approved.", rendered.EmailBody);
        Assert.Contains("<h2>Your leave request has been approved</h2>", rendered.EmailBody);
    }

    [Fact]
    public void Render_TaskAssigned_With_Required_And_Optional_Tokens_Produces_Expected_Wording()
    {
        var template = NotificationTemplateCatalogue.All[NotificationType.TaskAssigned];
        var tokens = new Dictionary<string, string>
        {
            ["TaskTitle"] = "Review leave request",
            ["TaskDescription"] = "Some detail",
        };

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsSuccess);
        var rendered = result.Value!;
        Assert.Equal("New task assigned: Review leave request", rendered.InAppTitle);
        Assert.Equal("Some detail", rendered.InAppBody);
        Assert.Equal("New task assigned: Review leave request", rendered.EmailSubject);
        Assert.Contains("Some detail", rendered.EmailBody);
    }

    [Fact]
    public void Render_Fails_When_Single_Required_Token_Missing()
    {
        var template = NotificationTemplateCatalogue.All[NotificationType.LeaveApproved];
        var tokens = new Dictionary<string, string> { ["StartDate"] = "3 Aug 2026" }; // EndDate missing

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsFailure);
        Assert.Contains("EndDate", result.Error.Message);
    }

    [Fact]
    public void Render_Fails_And_Lists_All_Missing_Required_Tokens_When_Multiple_Missing()
    {
        var template = NotificationTemplateCatalogue.All[NotificationType.DocumentExpiring];
        var tokens = new Dictionary<string, string>(); // none of the four required tokens supplied

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsFailure);
        Assert.Contains("DocumentTitle", result.Error.Message);
        Assert.Contains("DocumentTypeName", result.Error.Message);
        Assert.Contains("DaysUntilExpiry", result.Error.Message);
        Assert.Contains("ExpiryDate", result.Error.Message);
    }

    [Fact]
    public void Render_Omitted_Optional_Token_Substitutes_As_Empty_String()
    {
        var template = NotificationTemplateCatalogue.All[NotificationType.CandidateHired];
        // VacancyTitle is optional and deliberately omitted.
        var tokens = new Dictionary<string, string> { ["CandidateName"] = "Jane Smith" };

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsSuccess);
        var rendered = result.Value!;
        Assert.Equal("Candidate hired: Jane Smith", rendered.InAppTitle);
        Assert.Equal("Jane Smith has been hired for .", rendered.InAppBody);
    }

    [Fact]
    public void Render_InAppBody_Is_Null_When_Substituted_Result_Is_Entirely_Empty()
    {
        // TaskAssigned's InAppBodyTemplate is just "{TaskDescription}" — when TaskDescription (an
        // optional token) is omitted entirely, substitution leaves an empty string, which must
        // become null rather than "" on the rendered result.
        var template = NotificationTemplateCatalogue.All[NotificationType.TaskAssigned];
        var tokens = new Dictionary<string, string> { ["TaskTitle"] = "Review leave request" };

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.InAppBody);
    }

    [Fact]
    public void Render_InAppBody_Is_Null_When_Substituted_Result_Is_Whitespace_Only()
    {
        var template = new NotificationTemplateTestBuilder()
            .WithInAppBodyTemplate("   {OptionalToken}   ")
            .WithRequiredTokens("Required")
            .WithOptionalTokens("OptionalToken")
            .Build();
        var tokens = new Dictionary<string, string> { ["Required"] = "x" }; // OptionalToken omitted

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.InAppBody);
    }

    // Assumption: none of the six shipped catalogue templates currently take an arbitrary,
    // attacker-controlled token value (e.g. a free-text field a user fully controls with no
    // downstream sanitisation), so this test builds a synthetic template rather than relying on real
    // catalogue wording, to demonstrate the encode/don't-encode split in isolation.
    [Fact]
    public void Render_HtmlEncodes_Token_Value_In_EmailBody_Only()
    {
        var template = new NotificationTemplateTestBuilder()
            .WithInAppTitleTemplate("Title: {Value}")
            .WithInAppBodyTemplate("Body: {Value}")
            .WithEmailSubjectTemplate("Subject: {Value}")
            .WithEmailBodyTemplate("<p>{Value}</p>")
            .WithRequiredTokens("Value")
            .Build();
        var maliciousValue = "<script>alert(1)</script> Fish & Chips Ltd \"quoted\"";
        var tokens = new Dictionary<string, string> { ["Value"] = maliciousValue };

        var result = NotificationTemplateRenderer.Render(template, tokens);

        Assert.True(result.IsSuccess);
        var rendered = result.Value!;

        Assert.Equal($"Title: {maliciousValue}", rendered.InAppTitle);
        Assert.Equal($"Body: {maliciousValue}", rendered.InAppBody);
        Assert.Equal($"Subject: {maliciousValue}", rendered.EmailSubject);

        Assert.DoesNotContain("<script>", rendered.EmailBody);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", rendered.EmailBody);
        Assert.Contains("Fish &amp; Chips Ltd", rendered.EmailBody);
        Assert.Contains("&quot;quoted&quot;", rendered.EmailBody);
    }

    [Fact]
    public void Render_Is_Deterministic_For_Same_Template_And_Tokens()
    {
        var template = NotificationTemplateCatalogue.All[NotificationType.EmployeeCreated];
        var tokens = new Dictionary<string, string>
        {
            ["EmployeeName"] = "Alex Doe",
            ["JobTitle"] = "Engineer",
            ["Department"] = "Platform",
        };

        var first = NotificationTemplateRenderer.Render(template, tokens).Value!;
        var second = NotificationTemplateRenderer.Render(template, tokens).Value!;

        Assert.Equal(first.InAppTitle, second.InAppTitle);
        Assert.Equal(first.InAppBody, second.InAppBody);
        Assert.Equal(first.EmailSubject, second.EmailSubject);
        Assert.Equal(first.EmailBody, second.EmailBody);
    }
}

/// <summary>Minimal builder for synthetic NotificationTemplate instances used only by tests that need
/// to isolate rendering behaviour (e.g. HTML encoding) from real catalogue wording.</summary>
internal sealed class NotificationTemplateTestBuilder
{
    private string _inAppTitleTemplate = "Title";
    private string? _inAppBodyTemplate;
    private string _emailSubjectTemplate = "Subject";
    private string _emailBodyTemplate = "<p>Body</p>";
    private HashSet<string> _requiredTokens = [];
    private HashSet<string> _optionalTokens = [];

    public NotificationTemplateTestBuilder WithInAppTitleTemplate(string value)
    {
        _inAppTitleTemplate = value;
        return this;
    }

    public NotificationTemplateTestBuilder WithInAppBodyTemplate(string? value)
    {
        _inAppBodyTemplate = value;
        return this;
    }

    public NotificationTemplateTestBuilder WithEmailSubjectTemplate(string value)
    {
        _emailSubjectTemplate = value;
        return this;
    }

    public NotificationTemplateTestBuilder WithEmailBodyTemplate(string value)
    {
        _emailBodyTemplate = value;
        return this;
    }

    public NotificationTemplateTestBuilder WithRequiredTokens(params string[] tokens)
    {
        _requiredTokens = [.. tokens];
        return this;
    }

    public NotificationTemplateTestBuilder WithOptionalTokens(params string[] tokens)
    {
        _optionalTokens = [.. tokens];
        return this;
    }

    public NotificationTemplate Build() => new(
        Version: 1,
        InAppTitleTemplate: _inAppTitleTemplate,
        InAppBodyTemplate: _inAppBodyTemplate,
        EmailSubjectTemplate: _emailSubjectTemplate,
        EmailBodyTemplate: _emailBodyTemplate,
        RequiredTokens: _requiredTokens,
        OptionalTokens: _optionalTokens);
}
