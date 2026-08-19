using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Services;

namespace HR.Modules.Documents.Tests;

public class SharedCompanyDocumentAudienceMatcherTests
{
    private static readonly Guid CompanyId  = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();

    private static SharedCompanyDocumentAudienceRule Rule(SharedCompanyDocumentAudienceRuleType type, Guid targetId) =>
        SharedCompanyDocumentAudienceRule.Create(Guid.NewGuid(), CompanyId, DocumentId, type, targetId);

    [Fact]
    public void IsInAudience_Returns_True_For_Everyone_When_There_Are_No_Rules()
    {
        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience([], null, Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public void IsInAudience_Department_Rule_Matches_When_Employee_Is_In_The_Department()
    {
        var departmentId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Department, departmentId) };
        var profile = new EmployeeAudienceProfile(departmentId, null, null);

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, profile, Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public void IsInAudience_Department_Rule_Does_Not_Match_A_Different_Department()
    {
        var departmentId = Guid.NewGuid();
        var otherDepartmentId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Department, departmentId) };
        var profile = new EmployeeAudienceProfile(otherDepartmentId, null, null);

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, profile, Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void IsInAudience_Location_Rule_Matches_When_Employee_Is_At_The_Location()
    {
        var locationId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Location, locationId) };
        var profile = new EmployeeAudienceProfile(null, locationId, null);

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, profile, Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public void IsInAudience_Location_Rule_Does_Not_Match_A_Different_Location()
    {
        var locationId = Guid.NewGuid();
        var otherLocationId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Location, locationId) };
        var profile = new EmployeeAudienceProfile(null, otherLocationId, null);

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, profile, Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void IsInAudience_Position_Rule_Matches_When_Employee_Holds_The_Position()
    {
        var positionId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Position, positionId) };
        var profile = new EmployeeAudienceProfile(null, null, positionId);

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, profile, Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public void IsInAudience_Position_Rule_Does_Not_Match_A_Different_Position()
    {
        var positionId = Guid.NewGuid();
        var otherPositionId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Position, positionId) };
        var profile = new EmployeeAudienceProfile(null, null, otherPositionId);

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, profile, Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void IsInAudience_Employee_Rule_Matches_The_Named_Employee_Even_When_Profile_Is_Null()
    {
        var employeeId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Employee, employeeId) };

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, null, employeeId);

        Assert.True(result);
    }

    [Fact]
    public void IsInAudience_Rules_Are_OrEd_Together_Employee_Matching_Only_One_Of_Several_Rules_Is_Still_Included()
    {
        var departmentId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var rules = new[]
        {
            Rule(SharedCompanyDocumentAudienceRuleType.Department, departmentId),
            Rule(SharedCompanyDocumentAudienceRuleType.Position, positionId),
        };

        // Matches the Department rule only — a different position, so the Position rule alone would fail.
        var matchingProfile = new EmployeeAudienceProfile(departmentId, null, Guid.NewGuid());
        var matchingResult = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, matchingProfile, Guid.NewGuid());
        Assert.True(matchingResult);

        // Matches neither rule.
        var nonMatchingProfile = new EmployeeAudienceProfile(Guid.NewGuid(), null, Guid.NewGuid());
        var nonMatchingResult = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, nonMatchingProfile, Guid.NewGuid());
        Assert.False(nonMatchingResult);
    }

    [Fact]
    public void IsInAudience_Returns_False_When_Profile_Is_Null_And_No_Employee_Rule_Names_Them()
    {
        var departmentId = Guid.NewGuid();
        var rules = new[] { Rule(SharedCompanyDocumentAudienceRuleType.Department, departmentId) };

        var result = SharedCompanyDocumentAudienceMatcher.IsInAudience(rules, null, Guid.NewGuid());

        Assert.False(result);
    }
}
