using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class CandidateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Trims_And_Normalises_Optional_Fields()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), " Emma ", " Clarke ", " emma.clarke@example.com ", "  ", "   ", Now);

        Assert.Equal("Emma", candidate.FirstName);
        Assert.Equal("Clarke", candidate.LastName);
        Assert.Equal("emma.clarke@example.com", candidate.Email);
        Assert.Null(candidate.Phone);
        Assert.Null(candidate.ResumeUrl);
    }

    [Fact]
    public void UpdateDetails_Overwrites_Fields_And_UpdatedAt()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var later = Now.AddDays(1);

        candidate.UpdateDetails("Emma", "Clarke-Smith", "emma.clarke-smith@example.com", "+44 7700 900001", "https://example.com/resume.pdf", later);

        Assert.Equal("Clarke-Smith", candidate.LastName);
        Assert.Equal("emma.clarke-smith@example.com", candidate.Email);
        Assert.Equal("+44 7700 900001", candidate.Phone);
        Assert.Equal("https://example.com/resume.pdf", candidate.ResumeUrl);
        Assert.Equal(later, candidate.UpdatedAt);
    }

    [Fact]
    public void LinkToEmployee_Sets_EmployeeId()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var employeeId = Guid.NewGuid();

        candidate.LinkToEmployee(employeeId, Now);

        Assert.Equal(employeeId, candidate.EmployeeId);
    }

    [Fact]
    public void LinkToEmployee_When_Already_Linked_Throws()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.LinkToEmployee(Guid.NewGuid(), Now);

        Assert.Throws<InvalidOperationException>(() => candidate.LinkToEmployee(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Deactivate_Sets_IsActive_False_And_Deactivation_Fields()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var deactivatedBy = Guid.NewGuid();
        var later = Now.AddDays(1);

        candidate.Deactivate(deactivatedBy, "  No longer available  ", later);

        Assert.False(candidate.IsActive);
        Assert.Equal(later, candidate.DeactivatedAt);
        Assert.Equal(deactivatedBy, candidate.DeactivatedByUserId);
        Assert.Equal("No longer available", candidate.DeactivationReason);
        Assert.Equal(later, candidate.UpdatedAt);
    }

    [Fact]
    public void Deactivate_When_Already_Inactive_Throws()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.Deactivate(Guid.NewGuid(), "No longer available", Now);

        Assert.Throws<InvalidOperationException>(() => candidate.Deactivate(Guid.NewGuid(), "Another reason", Now.AddDays(1)));
    }

    [Fact]
    public void Reactivate_Sets_IsActive_True_And_Reactivation_Fields()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.Deactivate(Guid.NewGuid(), "No longer available", Now);
        var reactivatedBy = Guid.NewGuid();
        var later = Now.AddDays(2);

        candidate.Reactivate(reactivatedBy, later);

        Assert.True(candidate.IsActive);
        Assert.Equal(later, candidate.ReactivatedAt);
        Assert.Equal(reactivatedBy, candidate.ReactivatedByUserId);
        Assert.Equal(later, candidate.UpdatedAt);
    }

    [Fact]
    public void Reactivate_When_Already_Active_Throws()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);

        Assert.Throws<InvalidOperationException>(() => candidate.Reactivate(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Reactivate_Does_Not_Clear_Prior_Deactivation_History_Fields()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var deactivatedBy = Guid.NewGuid();
        candidate.Deactivate(deactivatedBy, "No longer available", Now);

        candidate.Reactivate(Guid.NewGuid(), Now.AddDays(1));

        Assert.Equal(Now, candidate.DeactivatedAt);
        Assert.Equal(deactivatedBy, candidate.DeactivatedByUserId);
        Assert.Equal("No longer available", candidate.DeactivationReason);
    }
}
