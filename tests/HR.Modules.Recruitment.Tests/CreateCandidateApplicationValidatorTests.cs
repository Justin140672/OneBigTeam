using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateCandidateApplication;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 3: request validation for creating a brand-new candidate and their
/// application to a vacancy in one call.
/// </summary>
public class CreateCandidateApplicationValidatorTests
{
    private readonly CreateCandidateApplicationValidator _validator = new();

    private static CreateCandidateApplicationRequest Valid(
        Guid? companyId = null,
        Guid? vacancyId = null,
        string firstName = "Emma",
        string lastName = "Clarke",
        string email = "emma.clarke@example.com",
        string? phone = null,
        string? resumeUrl = null,
        string? notes = null,
        ApplicationSource? source = null,
        Guid? recruiterId = null) =>
        new()
        {
            CompanyId                 = companyId ?? Guid.NewGuid(),
            VacancyId                 = vacancyId ?? Guid.NewGuid(),
            FirstName                 = firstName,
            LastName                  = lastName,
            Email                     = email,
            Phone                     = phone,
            ResumeUrl                 = resumeUrl,
            Notes                     = notes,
            Source                    = source,
            SourceExternalRecruiterId = recruiterId,
        };

    private static string EmailOfLength(int length)
    {
        const string domain = "@example.com";
        return new string('a', length - domain.Length) + domain;
    }

    [Fact]
    public void Validate_Passes_For_Minimal_Valid_Request()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_With_All_Optional_Fields_Populated()
    {
        var result = _validator.Validate(Valid(
            phone: "07700 900123",
            resumeUrl: "https://example.com/cv.pdf",
            notes: "Referred by the hiring manager.",
            source: ApplicationSource.Referral));

        Assert.True(result.IsValid);
    }

    // ----- CompanyId / VacancyId -----

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid(companyId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(Valid(vacancyId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.VacancyId));
    }

    // ----- FirstName -----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_When_FirstName_Is_Empty_Or_Whitespace(string firstName)
    {
        var result = _validator.Validate(Valid(firstName: firstName));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Is_Null()
    {
        var result = _validator.Validate(Valid(firstName: null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.FirstName));
    }

    [Fact]
    public void Validate_Passes_When_FirstName_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid(firstName: new string('A', 100)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid(firstName: new string('A', 101)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.FirstName));
    }

    // ----- LastName -----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_When_LastName_Is_Empty_Or_Whitespace(string lastName)
    {
        var result = _validator.Validate(Valid(lastName: lastName));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.LastName));
    }

    [Fact]
    public void Validate_Fails_When_LastName_Is_Null()
    {
        var result = _validator.Validate(Valid(lastName: null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.LastName));
    }

    [Fact]
    public void Validate_Passes_When_LastName_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid(lastName: new string('B', 100)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LastName_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid(lastName: new string('B', 101)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.LastName));
    }

    // ----- Email -----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_When_Email_Is_Empty_Or_Whitespace(string email)
    {
        var result = _validator.Validate(Valid(email: email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Null()
    {
        var result = _validator.Validate(Valid(email: null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Email));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("emma@")]
    public void Validate_Fails_When_Email_Format_Is_Invalid(string email)
    {
        var result = _validator.Validate(Valid(email: email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Email));
    }

    [Fact]
    public void Validate_Passes_When_Email_Is_Exactly_256_Characters()
    {
        var email = EmailOfLength(256);
        Assert.Equal(256, email.Length);

        var result = _validator.Validate(Valid(email: email));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Email_Exceeds_256_Characters()
    {
        var email = EmailOfLength(257);
        Assert.Equal(257, email.Length);

        var result = _validator.Validate(Valid(email: email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Email));
    }

    // ----- Phone -----

    [Fact]
    public void Validate_Passes_When_Phone_Is_Exactly_30_Characters()
    {
        var result = _validator.Validate(Valid(phone: new string('1', 30)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Phone_Exceeds_30_Characters()
    {
        var result = _validator.Validate(Valid(phone: new string('1', 31)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Phone));
    }

    // ----- ResumeUrl -----

    [Fact]
    public void Validate_Passes_When_ResumeUrl_Is_Exactly_500_Characters()
    {
        var result = _validator.Validate(Valid(resumeUrl: new string('u', 500)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ResumeUrl_Exceeds_500_Characters()
    {
        var result = _validator.Validate(Valid(resumeUrl: new string('u', 501)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.ResumeUrl));
    }

    // ----- Notes -----

    [Fact]
    public void Validate_Passes_When_Notes_Is_Exactly_2000_Characters()
    {
        var result = _validator.Validate(Valid(notes: new string('N', 2000)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_2000_Characters()
    {
        var result = _validator.Validate(Valid(notes: new string('N', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Notes));
    }

    // ----- Source / SourceExternalRecruiterId pairing -----

    [Fact]
    public void Validate_Passes_When_Source_Is_ExternalRecruiter_With_Recruiter_Id()
    {
        var result = _validator.Validate(Valid(source: ApplicationSource.ExternalRecruiter, recruiterId: Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Source_Is_ExternalRecruiter_Without_Recruiter_Id()
    {
        var result = _validator.Validate(Valid(source: ApplicationSource.ExternalRecruiter, recruiterId: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.SourceExternalRecruiterId));
    }

    [Fact]
    public void Validate_Fails_When_Source_Is_ExternalRecruiter_With_Empty_Guid_Recruiter_Id()
    {
        var result = _validator.Validate(Valid(source: ApplicationSource.ExternalRecruiter, recruiterId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.SourceExternalRecruiterId));
    }

    // ApplicationSource is internal, so a public theory cannot take it as a parameter (CS0051);
    // rows carry the enum member name and are parsed inside the test.
    [Theory]
    [InlineData(nameof(ApplicationSource.Unspecified))]
    [InlineData(nameof(ApplicationSource.Direct))]
    [InlineData(nameof(ApplicationSource.Referral))]
    [InlineData(nameof(ApplicationSource.JobBoard))]
    [InlineData(nameof(ApplicationSource.CareersSite))]
    public void Validate_Fails_When_Recruiter_Id_Supplied_For_Non_ExternalRecruiter_Source(string sourceName)
    {
        var source = Enum.Parse<ApplicationSource>(sourceName);

        var result = _validator.Validate(Valid(source: source, recruiterId: Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.SourceExternalRecruiterId));
    }

    [Fact]
    public void Validate_Fails_When_Recruiter_Id_Supplied_Without_Any_Source()
    {
        var result = _validator.Validate(Valid(source: null, recruiterId: Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.SourceExternalRecruiterId));
    }

    [Theory]
    [InlineData(nameof(ApplicationSource.Unspecified))]
    [InlineData(nameof(ApplicationSource.Direct))]
    [InlineData(nameof(ApplicationSource.Referral))]
    [InlineData(nameof(ApplicationSource.JobBoard))]
    [InlineData(nameof(ApplicationSource.CareersSite))]
    public void Validate_Passes_When_Non_ExternalRecruiter_Source_Has_No_Recruiter_Id(string sourceName)
    {
        var source = Enum.Parse<ApplicationSource>(sourceName);

        var result = _validator.Validate(Valid(source: source, recruiterId: null));

        Assert.True(result.IsValid);
    }

    // Guard: if a new ApplicationSource member is added, both recruiter-id rules must still hold for it
    // even before the theory rows above are updated.
    [Fact]
    public void Recruiter_Id_Rules_Hold_For_Every_Non_ExternalRecruiter_Source()
    {
        var nonExternalSources = Enum.GetValues<ApplicationSource>()
            .Where(s => s != ApplicationSource.ExternalRecruiter)
            .ToList();

        Assert.NotEmpty(nonExternalSources);

        foreach (var source in nonExternalSources)
        {
            var withRecruiter = _validator.Validate(Valid(source: source, recruiterId: Guid.NewGuid()));
            Assert.False(withRecruiter.IsValid, $"Expected failure for {source} with a recruiter id.");
            Assert.Contains(withRecruiter.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.SourceExternalRecruiterId));

            var withoutRecruiter = _validator.Validate(Valid(source: source, recruiterId: null));
            if (source == ApplicationSource.Internal)
            {
                // Internal recruitment Ticket 4: Internal is refused on its own (Source) rule, never on
                // the recruiter-id rule — see the dedicated Internal facts below.
                Assert.False(withoutRecruiter.IsValid, "Expected Internal to be rejected.");
                Assert.DoesNotContain(withoutRecruiter.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.SourceExternalRecruiterId));
                continue;
            }

            Assert.True(withoutRecruiter.IsValid, $"Expected success for {source} without a recruiter id.");
        }
    }

    // ----- Internal recruitment Ticket 4: Source "Internal" is never recruiter-selectable -----

    [Fact]
    public void Validate_Fails_When_Source_Is_Internal()
    {
        var result = _validator.Validate(Valid(source: ApplicationSource.Internal));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Source));
        Assert.Contains("Internal", error.ErrorMessage);
    }

    [Fact]
    public void Validate_Fails_Only_On_Source_When_Internal_And_Every_Other_Field_Valid()
    {
        var result = _validator.Validate(Valid(
            phone: "07700 900123",
            resumeUrl: "https://example.com/cv.pdf",
            notes: "Referred by the hiring manager.",
            source: ApplicationSource.Internal));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(CreateCandidateApplicationRequest.Source), error.PropertyName);
    }

    [Fact]
    public void Validate_Fails_When_Source_Is_Internal_With_A_Recruiter_Id()
    {
        var result = _validator.Validate(Valid(source: ApplicationSource.Internal, recruiterId: Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.Source));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateApplicationRequest.SourceExternalRecruiterId));
    }

    [Fact]
    public void Validate_Passes_When_Source_Is_Null()
    {
        var result = _validator.Validate(Valid(source: null));

        Assert.True(result.IsValid);
    }
}
