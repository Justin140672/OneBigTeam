using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 4: domain rules for the employee-linked Candidate —
/// <see cref="Candidate.CreateForEmployee"/>, <see cref="Candidate.SyncEmployeeIdentity"/> and
/// <see cref="Candidate.DescribeEmployeeIdentityViolation"/>.
/// </summary>
public class CandidateEmployeeLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private static Candidate CreateLinked(Guid employeeId, string? phone = "07700 900456") =>
        Candidate.CreateForEmployee(
            Guid.NewGuid(), Guid.NewGuid(), employeeId, "Priya", "Shah", "priya.shah@acme.example", phone, Now);


    [Fact]
    public void DescribeEmployeeIdentityViolation_Returns_Null_For_Valid_Identity()
    {
        Assert.Null(Candidate.DescribeEmployeeIdentityViolation("Priya", "Shah", "priya.shah@acme.example"));
    }

    [Fact]
    public void DescribeEmployeeIdentityViolation_Returns_Null_At_Exact_Max_Lengths()
    {
        var email = new string('a', Candidate.EmailMaxLength - "@acme.example".Length) + "@acme.example";

        Assert.Equal(Candidate.EmailMaxLength, email.Length);
        Assert.Null(Candidate.DescribeEmployeeIdentityViolation(
            new string('F', Candidate.FirstNameMaxLength),
            new string('L', Candidate.LastNameMaxLength),
            email));
    }

    [Fact]
    public void DescribeEmployeeIdentityViolation_Measures_Trimmed_Length()
    {
        // Surrounding whitespace is trimmed before storage, so it must not count towards the limit.
        Assert.Null(Candidate.DescribeEmployeeIdentityViolation(
            "  " + new string('F', Candidate.FirstNameMaxLength) + "  ",
            "Shah",
            "priya.shah@acme.example"));
    }

    [Theory]
    [InlineData("", "Shah", "priya.shah@acme.example")]
    [InlineData("   ", "Shah", "priya.shah@acme.example")]
    [InlineData("Priya", "", "priya.shah@acme.example")]
    [InlineData("Priya", "   ", "priya.shah@acme.example")]
    [InlineData("Priya", "Shah", "")]
    [InlineData("Priya", "Shah", "   ")]
    public void DescribeEmployeeIdentityViolation_Returns_Reason_For_Blank_Fields(string firstName, string lastName, string workEmail)
    {
        Assert.False(string.IsNullOrWhiteSpace(Candidate.DescribeEmployeeIdentityViolation(firstName, lastName, workEmail)));
    }

    [Fact]
    public void DescribeEmployeeIdentityViolation_Returns_Reason_When_FirstName_Exceeds_Max_By_One()
    {
        Assert.NotNull(Candidate.DescribeEmployeeIdentityViolation(
            new string('F', Candidate.FirstNameMaxLength + 1), "Shah", "priya.shah@acme.example"));
    }

    [Fact]
    public void DescribeEmployeeIdentityViolation_Returns_Reason_When_LastName_Exceeds_Max_By_One()
    {
        Assert.NotNull(Candidate.DescribeEmployeeIdentityViolation(
            "Priya", new string('L', Candidate.LastNameMaxLength + 1), "priya.shah@acme.example"));
    }

    [Fact]
    public void DescribeEmployeeIdentityViolation_Returns_Reason_When_Email_Exceeds_Max_By_One()
    {
        var email = new string('a', Candidate.EmailMaxLength + 1 - "@acme.example".Length) + "@acme.example";

        Assert.Equal(Candidate.EmailMaxLength + 1, email.Length);
        Assert.NotNull(Candidate.DescribeEmployeeIdentityViolation("Priya", "Shah", email));
    }


    [Fact]
    public void CreateForEmployee_Links_Employee_Trims_Identity_And_Is_Active()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var candidate = Candidate.CreateForEmployee(
            id, companyId, employeeId, "  Priya ", " Shah  ", "  Priya.Shah@Acme.example ", " 07700 900456 ", Now);

        Assert.Equal(id, candidate.Id);
        Assert.Equal(companyId, candidate.CompanyId);
        Assert.Equal(employeeId, candidate.EmployeeId);
        Assert.Equal("Priya", candidate.FirstName);
        Assert.Equal("Shah", candidate.LastName);
        Assert.Equal("Priya.Shah@Acme.example", candidate.Email);
        Assert.Equal("07700 900456", candidate.Phone);
        Assert.Null(candidate.ResumeUrl);
        Assert.True(candidate.IsActive);
        Assert.Null(candidate.PurgedAt);
        Assert.Equal(1, candidate.Version);
        Assert.Equal(Now, candidate.CreatedAt);
        Assert.Equal(Now, candidate.UpdatedAt);
    }

    [Fact]
    public void CreateForEmployee_Keeps_Phone_At_Exactly_Max_Length()
    {
        var phone = new string('7', Candidate.PhoneMaxLength);

        var candidate = CreateLinked(Guid.NewGuid(), phone);

        Assert.Equal(phone, candidate.Phone);
    }

    [Fact]
    public void CreateForEmployee_Drops_Phone_Longer_Than_Max_Length()
    {
        var candidate = CreateLinked(Guid.NewGuid(), new string('7', Candidate.PhoneMaxLength + 1));

        Assert.Null(candidate.Phone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateForEmployee_Stores_Null_For_Missing_Phone(string? phone)
    {
        var candidate = CreateLinked(Guid.NewGuid(), phone);

        Assert.Null(candidate.Phone);
    }

    [Fact]
    public void CreateForEmployee_Throws_ArgumentException_For_Empty_EmployeeId()
    {
        var ex = Assert.Throws<ArgumentException>(() => Candidate.CreateForEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Priya", "Shah", "priya.shah@acme.example", null, Now));

        Assert.Equal("employeeId", ex.ParamName);
    }

    [Theory]
    [InlineData("", "Shah", "priya.shah@acme.example")]
    [InlineData("   ", "Shah", "priya.shah@acme.example")]
    [InlineData("Priya", "", "priya.shah@acme.example")]
    [InlineData("Priya", "  ", "priya.shah@acme.example")]
    [InlineData("Priya", "Shah", "")]
    [InlineData("Priya", "Shah", "  ")]
    public void CreateForEmployee_Throws_For_Blank_Identity(string firstName, string lastName, string workEmail)
    {
        Assert.Throws<InvalidOperationException>(() => Candidate.CreateForEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), firstName, lastName, workEmail, null, Now));
    }

    [Fact]
    public void CreateForEmployee_Throws_When_FirstName_Too_Long()
    {
        Assert.Throws<InvalidOperationException>(() => Candidate.CreateForEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new string('F', Candidate.FirstNameMaxLength + 1), "Shah", "priya.shah@acme.example", null, Now));
    }

    [Fact]
    public void CreateForEmployee_Throws_When_LastName_Too_Long()
    {
        Assert.Throws<InvalidOperationException>(() => Candidate.CreateForEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Priya", new string('L', Candidate.LastNameMaxLength + 1), "priya.shah@acme.example", null, Now));
    }

    [Fact]
    public void CreateForEmployee_Throws_When_Email_Too_Long()
    {
        var email = new string('a', Candidate.EmailMaxLength + 1 - "@acme.example".Length) + "@acme.example";

        Assert.Throws<InvalidOperationException>(() => Candidate.CreateForEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Priya", "Shah", email, null, Now));
    }

    [Fact]
    public void CreateForEmployee_Accepts_Name_And_Email_At_Exact_Max_Lengths()
    {
        var email = new string('a', Candidate.EmailMaxLength - "@acme.example".Length) + "@acme.example";

        var candidate = Candidate.CreateForEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new string('F', Candidate.FirstNameMaxLength),
            new string('L', Candidate.LastNameMaxLength),
            email, null, Now);

        Assert.Equal(Candidate.FirstNameMaxLength, candidate.FirstName.Length);
        Assert.Equal(Candidate.LastNameMaxLength, candidate.LastName.Length);
        Assert.Equal(email, candidate.Email);
    }


    [Fact]
    public void SyncEmployeeIdentity_Returns_False_And_Leaves_UpdatedAt_When_Unchanged()
    {
        var employeeId = Guid.NewGuid();
        var candidate = CreateLinked(employeeId);

        var changed = candidate.SyncEmployeeIdentity(
            employeeId, " Priya ", "Shah ", " priya.shah@acme.example", " 07700 900456 ", Now.AddDays(1));

        Assert.False(changed);
        Assert.Equal(Now, candidate.UpdatedAt);
    }

    [Fact]
    public void SyncEmployeeIdentity_Returns_True_And_Updates_When_Name_Changed()
    {
        var employeeId = Guid.NewGuid();
        var candidate = CreateLinked(employeeId);
        var later = Now.AddDays(1);

        var changed = candidate.SyncEmployeeIdentity(
            employeeId, " Priya ", " Shah-Patel ", "priya.shah-patel@acme.example", "07700 900999", later);

        Assert.True(changed);
        Assert.Equal("Priya", candidate.FirstName);
        Assert.Equal("Shah-Patel", candidate.LastName);
        Assert.Equal("priya.shah-patel@acme.example", candidate.Email);
        Assert.Equal("07700 900999", candidate.Phone);
        Assert.Equal(later, candidate.UpdatedAt);
    }

    [Fact]
    public void SyncEmployeeIdentity_Returns_True_When_Only_Email_Case_Changed()
    {
        var employeeId = Guid.NewGuid();
        var candidate = CreateLinked(employeeId);

        var changed = candidate.SyncEmployeeIdentity(
            employeeId, "Priya", "Shah", "Priya.Shah@acme.example", "07700 900456", Now.AddDays(1));

        Assert.True(changed);
        Assert.Equal("Priya.Shah@acme.example", candidate.Email);
    }

    [Fact]
    public void SyncEmployeeIdentity_Returns_True_When_Only_Phone_Removed()
    {
        var employeeId = Guid.NewGuid();
        var candidate = CreateLinked(employeeId);

        var changed = candidate.SyncEmployeeIdentity(
            employeeId, "Priya", "Shah", "priya.shah@acme.example", null, Now.AddDays(1));

        Assert.True(changed);
        Assert.Null(candidate.Phone);
    }

    [Fact]
    public void SyncEmployeeIdentity_Drops_Overlong_Phone()
    {
        var employeeId = Guid.NewGuid();
        var candidate = CreateLinked(employeeId);

        var changed = candidate.SyncEmployeeIdentity(
            employeeId, "Priya", "Shah", "priya.shah@acme.example", new string('7', Candidate.PhoneMaxLength + 1), Now.AddDays(1));

        Assert.True(changed);
        Assert.Null(candidate.Phone);
    }

    [Fact]
    public void SyncEmployeeIdentity_Leaves_ResumeUrl_Untouched()
    {
        var employeeId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Priya", "Shah", "priya.shah@acme.example", null, "https://example.com/cv.pdf", Now);
        candidate.LinkToEmployee(employeeId, Now);

        candidate.SyncEmployeeIdentity(employeeId, "Priya", "Shah-Patel", "priya.shah@acme.example", null, Now.AddDays(1));

        Assert.Equal("https://example.com/cv.pdf", candidate.ResumeUrl);
    }

    [Fact]
    public void SyncEmployeeIdentity_Throws_When_Candidate_Not_Linked()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Priya", "Shah", "priya.shah@acme.example", null, null, Now);

        Assert.Throws<InvalidOperationException>(() => candidate.SyncEmployeeIdentity(
            Guid.NewGuid(), "Priya", "Shah", "priya.shah@acme.example", null, Now.AddDays(1)));
    }

    [Fact]
    public void SyncEmployeeIdentity_Throws_When_Linked_To_Another_Employee_And_Changes_Nothing()
    {
        var candidate = CreateLinked(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => candidate.SyncEmployeeIdentity(
            Guid.NewGuid(), "Someone", "Else", "someone.else@acme.example", null, Now.AddDays(1)));

        Assert.Equal("Priya", candidate.FirstName);
        Assert.Equal("priya.shah@acme.example", candidate.Email);
        Assert.Equal(Now, candidate.UpdatedAt);
    }

    [Fact]
    public void SyncEmployeeIdentity_Throws_When_Candidate_Purged_And_Does_Not_Restore_Data()
    {
        var employeeId = Guid.NewGuid();
        var candidate = CreateLinked(employeeId);
        candidate.Purge(Guid.NewGuid(), Now.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => candidate.SyncEmployeeIdentity(
            employeeId, "Priya", "Shah", "priya.shah@acme.example", null, Now.AddDays(2)));

        Assert.Equal("[purged]", candidate.FirstName);
        Assert.Equal("[purged]", candidate.LastName);
    }

    [Fact]
    public void SyncEmployeeIdentity_Throws_For_Invalid_Identity_And_Leaves_Candidate_Unchanged()
    {
        var employeeId = Guid.NewGuid();
        var candidate = CreateLinked(employeeId);

        Assert.Throws<InvalidOperationException>(() => candidate.SyncEmployeeIdentity(
            employeeId, "Priya", "Shah", "   ", null, Now.AddDays(1)));

        Assert.Equal("priya.shah@acme.example", candidate.Email);
        Assert.Equal(Now, candidate.UpdatedAt);
    }


    [Fact]
    public void LinkToEmployee_On_Employee_Linked_Candidate_Throws()
    {
        var candidate = CreateLinked(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => candidate.LinkToEmployee(Guid.NewGuid(), Now.AddDays(1)));
    }
}
