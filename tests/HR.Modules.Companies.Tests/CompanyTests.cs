using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanyTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Create_Sets_Status_To_PendingVerification()
    {
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", Now);

        Assert.False(company.IsActive);
        Assert.Equal(Now, company.CreatedAt);
        Assert.Equal(Now, company.UpdatedAt);
    }

    [Fact]
    public void Activate_Sets_Status_To_Active()
    {
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", Now);
        var activatedAt = Now.AddMinutes(5);

        company.Activate(activatedAt);

        Assert.True(company.IsActive);
        Assert.Equal(activatedAt, company.UpdatedAt);
    }

    [Fact]
    public void Deactivate_Sets_Status_To_Deactivated()
    {
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", Now);
        var activatedAt = Now.AddMinutes(5);
        company.Activate(activatedAt);

        var deactivatedAt = Now.AddMinutes(10);
        company.Deactivate(deactivatedAt);

        Assert.False(company.IsActive);
        Assert.Equal(deactivatedAt, company.UpdatedAt);
    }

    [Fact]
    public void Deactivate_From_PendingVerification_Sets_Status_To_Deactivated()
    {
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", Now);

        var deactivatedAt = Now.AddMinutes(10);
        company.Deactivate(deactivatedAt);

        Assert.False(company.IsActive);
        Assert.Equal(deactivatedAt, company.UpdatedAt);
    }
}
