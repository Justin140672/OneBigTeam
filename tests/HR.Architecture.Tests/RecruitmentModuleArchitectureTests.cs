using System.Reflection;
using HR.Modules.Recruitment;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class RecruitmentModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(RecruitmentModule).Assembly;

    private static RecruitmentDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new RecruitmentDbContext(options);
    }

    [Fact]
    public void Recruitment_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "RecruitmentModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Recruitment module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Recruitment_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "RecruitmentDbContext");

        Assert.False(dbContextType.IsPublic, "RecruitmentDbContext must be internal, not public.");
    }

    [Theory]
    [InlineData(typeof(Vacancy))]
    [InlineData(typeof(Candidate))]
    [InlineData(typeof(Application))]
    [InlineData(typeof(Interview))]
    [InlineData(typeof(CandidateDocument))]
    public void Entity_Is_Not_Public(Type entityType)
    {
        Assert.False(entityType.IsPublic, $"{entityType.Name} entity must be internal, not public.");
    }

    [Fact]
    public void Recruitment_DbContext_Uses_Recruitment_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("recruitment", context.Model.GetDefaultSchema());
    }

    [Theory]
    [InlineData(typeof(Vacancy), "vacancies")]
    [InlineData(typeof(Candidate), "candidates")]
    [InlineData(typeof(Application), "applications")]
    [InlineData(typeof(Interview), "interviews")]
    [InlineData(typeof(CandidateDocument), "candidate_documents")]
    public void Entity_Maps_To_Correct_Table_And_Schema(Type clrType, string expectedTable)
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(clrType)!;

        Assert.Equal(expectedTable, entityType.GetTableName());
        Assert.Equal("recruitment", entityType.GetSchema());
    }

    [Theory]
    [InlineData(typeof(Vacancy))]
    [InlineData(typeof(Candidate))]
    [InlineData(typeof(Application))]
    [InlineData(typeof(Interview))]
    [InlineData(typeof(CandidateDocument))]
    public void Entity_Primary_Key_Is_Guid(Type clrType)
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(clrType)!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Theory]
    [InlineData(typeof(Vacancy))]
    [InlineData(typeof(Candidate))]
    [InlineData(typeof(Application))]
    [InlineData(typeof(Interview))]
    [InlineData(typeof(CandidateDocument))]
    public void Entity_All_Columns_Are_snake_case(Type clrType)
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(clrType)!;

        var violations = entityType
            .GetProperties()
            .Select(p => p.GetColumnName())
            .Where(name => name.Any(char.IsUpper))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Column names must be snake_case. Violations: {string.Join(", ", violations)}");
    }
}
