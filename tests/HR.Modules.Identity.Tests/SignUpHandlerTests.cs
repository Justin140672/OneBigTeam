using System.Security.Cryptography;
using System.Text;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class SignUpHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(Now);

    private static SignUpRequest ValidRequest() => new(
        CompanyName: "Acme Corp",
        AdminFirstName: "Ada",
        AdminLastName: "Lovelace",
        AdminEmail: $"ada-{Guid.NewGuid():N}@example.com",
        Password: "P@ssw0rd123");

    private SignUpHandler BuildHandler(FakeCompanyProvisioner provisioner) =>
        new(fixture.BuildContext(), provisioner, Clock);

    [Fact]
    public async Task HandleAsync_Returns_Success_And_Provisions_Company_On_Happy_Path()
    {
        var provisioner = new FakeCompanyProvisioner();
        var handler = BuildHandler(provisioner);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, provisioner.CallCount);
        Assert.Contains("Acme Corp", provisioner.ProvisionedCompanyNames);
        Assert.Equal(request.AdminEmail, result.Value!.Email);
        Assert.Equal(request.AdminFirstName, result.Value.FirstName);
        Assert.Equal(request.AdminLastName, result.Value.LastName);
        Assert.NotEqual(Guid.Empty, result.Value.UserId);
    }

    [Fact]
    public async Task HandleAsync_Creates_ApplicationUser_With_CompanyAdministrator_And_Employee_Roles()
    {
        var provisioner = new FakeCompanyProvisioner();
        var handler = BuildHandler(provisioner);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db = fixture.BuildContext();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == result.Value!.UserId);
        Assert.NotNull(user);
        Assert.Equal(request.AdminEmail, user!.Email);
        Assert.Equal(request.AdminFirstName, user.FirstName);
        Assert.Equal(request.AdminLastName, user.LastName);
        Assert.False(user.IsEmailConfirmed);

        // Every seeded persona carries SystemRoles.Employee alongside their specific role — it's
        // the floor role required by "role:employee", which gates core session endpoints
        // (GetMe, GetCompany, etc.) that AppSession depends on for every page.
        var roles = await db.UserRoles.Where(r => r.UserId == user.Id).Select(r => r.RoleId).ToListAsync();
        Assert.Contains(SystemRoles.CompanyAdministrator, roles);
        Assert.Contains(SystemRoles.Employee, roles);
        Assert.Equal(2, roles.Count);
    }

    [Fact]
    public async Task HandleAsync_Hashes_Password_Using_Sha256_Base64()
    {
        var provisioner = new FakeCompanyProvisioner();
        var handler = BuildHandler(provisioner);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var expectedHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(request.Password)));

        await using var db = fixture.BuildContext();
        var user = await db.Users.SingleAsync(u => u.Id == result.Value!.UserId);
        Assert.Equal(expectedHash, user.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Email_Already_In_Use()
    {
        var existingEmail = $"existing-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(
                Guid.NewGuid(), existingEmail, "hash", "Existing", "User", Now));
            await db.SaveChangesAsync();
        }

        var provisioner = new FakeCompanyProvisioner();
        var handler = BuildHandler(provisioner);
        var request = ValidRequest() with { AdminEmail = existingEmail.ToUpperInvariant() };

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(0, provisioner.CallCount);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_User_When_Email_Already_In_Use()
    {
        var existingEmail = $"existing-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(
                Guid.NewGuid(), existingEmail, "hash", "Existing", "User", Now));
            await db.SaveChangesAsync();
        }

        var provisioner = new FakeCompanyProvisioner();
        var handler = BuildHandler(provisioner);
        var request = ValidRequest() with { AdminEmail = existingEmail };

        await handler.HandleAsync(request, CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var userCount = await db2.Users.CountAsync(u => u.Email == existingEmail);
        Assert.Equal(1, userCount);
    }
}
