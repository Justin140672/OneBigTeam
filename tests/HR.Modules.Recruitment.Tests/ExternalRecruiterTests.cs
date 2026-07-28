using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class ExternalRecruiterTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_IsActive_True_By_Default()
    {
        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Acme Recruiting", "Jane Smith", "jane@acme.com", "01234 567890", "https://acme.com", "Notes", Now);

        Assert.True(recruiter.IsActive);
        Assert.Equal("Acme Recruiting", recruiter.AgencyName);
        Assert.Equal(Now, recruiter.CreatedAt);
        Assert.Equal(Now, recruiter.UpdatedAt);
    }

    [Fact]
    public void Create_Trims_Names_And_Nulls_Out_Whitespace_Optional_Fields()
    {
        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), Guid.NewGuid(), "  Acme Recruiting  ", "   ", "   ", "   ", "   ", "   ", Now);

        Assert.Equal("Acme Recruiting", recruiter.AgencyName);
        Assert.Null(recruiter.ContactName);
        Assert.Null(recruiter.ContactEmail);
        Assert.Null(recruiter.ContactTelephone);
        Assert.Null(recruiter.Website);
        Assert.Null(recruiter.Notes);
    }

    [Fact]
    public void UpdateDetails_Updates_Fields_And_UpdatedAt()
    {
        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Acme Recruiting", null, null, null, null, null, Now);
        var later = Now.AddDays(1);

        recruiter.UpdateDetails("New Agency Name", "John Doe", "john@newagency.com", "9999", "https://newagency.com", "Updated notes", later);

        Assert.Equal("New Agency Name", recruiter.AgencyName);
        Assert.Equal("John Doe", recruiter.ContactName);
        Assert.Equal("john@newagency.com", recruiter.ContactEmail);
        Assert.Equal("9999", recruiter.ContactTelephone);
        Assert.Equal("https://newagency.com", recruiter.Website);
        Assert.Equal("Updated notes", recruiter.Notes);
        Assert.Equal(later, recruiter.UpdatedAt);
    }

    [Fact]
    public void SetActiveStatus_Never_Removes_The_Row_Only_Flips_IsActive()
    {
        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Acme Recruiting", null, null, null, null, null, Now);
        var later = Now.AddDays(1);

        recruiter.SetActiveStatus(false, later);

        Assert.False(recruiter.IsActive);
        Assert.Equal(later, recruiter.UpdatedAt);
        // Row still fully populated/addressable — nothing was deleted.
        Assert.Equal("Acme Recruiting", recruiter.AgencyName);

        var evenLater = later.AddDays(1);
        recruiter.SetActiveStatus(true, evenLater);

        Assert.True(recruiter.IsActive);
        Assert.Equal(evenLater, recruiter.UpdatedAt);
    }
}
