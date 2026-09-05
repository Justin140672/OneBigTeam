using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Covers the anonymous marketing contact form relay endpoint (POST /api/contact, HR.Api/Program.cs).
/// Validation, honeypot, and happy-path tests run against <see cref="ContactApiWebApplicationFactory"/>,
/// which configures Marketing:ContactForm:RecipientEmail to a test value; the "recipient not
/// configured" test instead uses the shared <see cref="ApiWebApplicationFactory"/>, whose default
/// appsettings.json intentionally leaves that setting blank.
/// </summary>
// Part of the "Integration" collection so ApiWebApplicationFactory's shared Postgres container is
// started (and its connection string exported) before ContactApiWebApplicationFactory builds its
// host — the latter no longer runs a container of its own.
// IClassFixture shares one ContactApiWebApplicationFactory (one host boot + migration run) across
// every test method in this class, instead of xUnit's default of a new class instance (and thus a
// new factory, since it was a field initializer) per test method.
[Collection("Integration")]
public sealed class ContactEndpointTests : IClassFixture<ContactApiWebApplicationFactory>
{
    private readonly ContactApiWebApplicationFactory _factory;

    // ApiWebApplicationFactory is injected only to force collection ordering: the shared collection
    // fixture has already started the Postgres container and set ConnectionStrings__hr by the time
    // this runs.
    public ContactEndpointTests(ContactApiWebApplicationFactory factory, ApiWebApplicationFactory _)
    {
        _factory = factory;
        _factory.EmailSender.Clear();
    }

    private static object ValidContactRequest(
        string name = "Ada Lovelace",
        string email = "ada@example.com",
        string company = "Acme Ltd",
        int? employeeCount = 42,
        string message = "We'd like a demo, please.",
        string? website = null) => new
        {
            name,
            email,
            company,
            employeeCount,
            message,
            website,
        };

    [Fact]
    public async Task Post_Contact_Returns_Ok_And_Sends_Email_For_Valid_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sent = Assert.Single(_factory.EmailSender.Sent);
        Assert.Equal(ContactApiWebApplicationFactory.RecipientEmail, sent.ToEmail);
        Assert.Contains("Acme Ltd", sent.Subject);
        Assert.Contains("Ada Lovelace", sent.HtmlBody);
        Assert.Contains("ada@example.com", sent.HtmlBody);
        Assert.Contains("like a demo, please.", sent.HtmlBody);
    }

    [Fact]
    public async Task Post_Contact_Does_Not_Require_Authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest());

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Contact_Returns_Ok_But_Does_Not_Send_Email_When_Honeypot_Is_Filled()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/contact",
            ValidContactRequest(website: "https://spambot.example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.EmailSender.Sent);
    }

    [Theory]
    [InlineData(null, "name")]
    [InlineData("", "name")]
    [InlineData("   ", "name")]
    public async Task Post_Contact_Returns_BadRequest_When_Name_Is_Blank(string? name, string expectedField)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest(name: name!));

        await AssertValidationErrorAsync(response, expectedField);
        Assert.Empty(_factory.EmailSender.Sent);
    }

    [Fact]
    public async Task Post_Contact_Returns_BadRequest_When_Name_Exceeds_Max_Length()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/contact",
            ValidContactRequest(name: new string('a', 201)));

        await AssertValidationErrorAsync(response, "name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.example.com")]
    public async Task Post_Contact_Returns_BadRequest_When_Email_Is_Invalid(string? email)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest(email: email!));

        await AssertValidationErrorAsync(response, "email");
        Assert.Empty(_factory.EmailSender.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_Contact_Returns_Ok_When_Company_Is_Blank(string? company)
    {
        // Company name is marked "(optional)" on the marketing form (Contact.razor) and
        // Program.cs's ValidateContactRequest only rejects it for exceeding the max length, never
        // for being blank — this mirrors that intentional design.
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest(company: company!));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.EmailSender.Sent);
    }

    [Fact]
    public async Task Post_Contact_Returns_BadRequest_When_Company_Exceeds_Max_Length()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/contact",
            ValidContactRequest(company: new string('a', 201)));

        await AssertValidationErrorAsync(response, "company");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Post_Contact_Returns_BadRequest_When_EmployeeCount_Is_Invalid(int? employeeCount)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest(employeeCount: employeeCount));

        await AssertValidationErrorAsync(response, "employeeCount");
        Assert.Empty(_factory.EmailSender.Sent);
    }

    [Fact]
    public async Task Post_Contact_Returns_Ok_When_EmployeeCount_Is_Null()
    {
        // Approximate employee count is marked "(optional)" on the marketing form (Contact.razor) —
        // Program.cs's ValidateContactRequest only rejects it when provided and < 1, never for
        // being absent.
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest(employeeCount: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.EmailSender.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_Contact_Returns_BadRequest_When_Message_Is_Blank(string? message)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidContactRequest(message: message!));

        await AssertValidationErrorAsync(response, "message");
        Assert.Empty(_factory.EmailSender.Sent);
    }

    [Fact]
    public async Task Post_Contact_Returns_BadRequest_When_Message_Exceeds_Max_Length()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/contact",
            ValidContactRequest(message: new string('a', 4001)));

        await AssertValidationErrorAsync(response, "message");
    }

    [Fact]
    public async Task Post_Contact_Returns_BadRequest_With_All_Field_Errors_When_Every_Field_Is_Missing()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", new { });

        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", errors.Keys);
        Assert.Contains("email", errors.Keys);
        Assert.Contains("message", errors.Keys);
        // Company and employeeCount are optional (Contact.razor marks both "(optional)") — missing
        // entirely is valid, so they're deliberately absent from the expected error set here.
        Assert.DoesNotContain("company", errors.Keys);
        Assert.DoesNotContain("employeeCount", errors.Keys);
    }

    private static async Task AssertValidationErrorAsync(HttpResponseMessage response, string expectedField)
    {
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedField, errors.Keys);
    }

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var errorsElement = document.RootElement.GetProperty("errors");

        var errors = new Dictionary<string, string[]>();
        foreach (var property in errorsElement.EnumerateObject())
        {
            errors[property.Name] = property.Value.EnumerateArray().Select(v => v.GetString()!).ToArray();
        }

        return errors;
    }
}

/// <summary>
/// Covers the "contact form is not configured" branch using the shared, collection-fixtured
/// ApiWebApplicationFactory, whose default appsettings.json leaves
/// Marketing:ContactForm:RecipientEmail intentionally blank.
/// </summary>
[Collection("Integration")]
public sealed class ContactEndpointNotConfiguredTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ContactEndpointNotConfiguredTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Contact_Returns_ServiceUnavailable_When_RecipientEmail_Is_Not_Configured()
    {
        using var client = _factory.CreateClient();
        // The shared factory's FakeEmailSender is a singleton accumulating across the whole
        // assembly (other tests in this collection send emails too), so compare counts before/after
        // rather than asserting an empty collection outright.
        var sentCountBefore = _factory.EmailSender.Sent.Count;

        var response = await client.PostAsJsonAsync("/api/contact", new
        {
            name = "Ada Lovelace",
            email = "ada@example.com",
            company = "Acme Ltd",
            employeeCount = 42,
            message = "We'd like a demo, please.",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(sentCountBefore, _factory.EmailSender.Sent.Count);
    }
}
