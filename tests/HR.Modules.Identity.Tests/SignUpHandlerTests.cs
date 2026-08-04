using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class SignUpHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(Now);
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    private static SignUpRequest ValidRequest() => new(
        CompanyName: "Acme Corp",
        AdminFirstName: "Ada",
        AdminLastName: "Lovelace",
        AdminEmail: $"ada-{Guid.NewGuid():N}@example.com",
        Password: "P@ssw0rd123");

    private sealed record Dependencies(
        FakeCompanyProvisioner Provisioner,
        FakeCompanyDefaultDataSeeder DefaultDataSeeder,
        FakeEmployeeProvisioningService EmployeeProvisioningService,
        FakeSupabaseAuthGateway SupabaseAuthGateway,
        FakeAuditEventPublisher AuditEventPublisher);

    private SignUpHandler BuildHandler(Dependencies dependencies) =>
        new(
            fixture.BuildContext(),
            dependencies.Provisioner,
            dependencies.DefaultDataSeeder,
            dependencies.EmployeeProvisioningService,
            dependencies.SupabaseAuthGateway,
            dependencies.AuditEventPublisher,
            EmptyConfiguration,
            Clock,
            NullLogger<SignUpHandler>.Instance);

    private static Dependencies BuildDependencies() => new(
        new FakeCompanyProvisioner(),
        new FakeCompanyDefaultDataSeeder(),
        new FakeEmployeeProvisioningService(),
        new FakeSupabaseAuthGateway(),
        new FakeAuditEventPublisher());

    [Fact]
    public async Task HandleAsync_Returns_Success_And_Provisions_Company_On_Happy_Path()
    {
        var deps = BuildDependencies();
        var handler = BuildHandler(deps);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, deps.Provisioner.CallCount);
        Assert.Contains("Acme Corp", deps.Provisioner.ProvisionedCompanyNames);
        Assert.Equal(request.AdminEmail, result.Value!.Email);
        Assert.Equal(request.AdminFirstName, result.Value.FirstName);
        Assert.Equal(request.AdminLastName, result.Value.LastName);
        Assert.NotEqual(Guid.Empty, result.Value.UserId);
    }

    [Fact]
    public async Task HandleAsync_Runs_Orchestration_Steps_In_Order_On_Happy_Path()
    {
        var deps = BuildDependencies();
        var handler = BuildHandler(deps);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Company provisioned first.
        Assert.Equal(1, deps.Provisioner.CallCount);

        // Default data seeded for the newly provisioned company.
        Assert.Equal(1, deps.DefaultDataSeeder.CallCount);
        Assert.Equal(result.Value!.CompanyId, deps.DefaultDataSeeder.SeededCompanyIds.Single());

        // Admin employee created using the seeded default ids.
        Assert.Equal(1, deps.EmployeeProvisioningService.CallCount);
        var employeeRequest = deps.EmployeeProvisioningService.Requests.Single();
        Assert.Equal(result.Value.CompanyId, employeeRequest.CompanyId);
        Assert.Equal(deps.DefaultDataSeeder.ResultToReturn.DepartmentId, employeeRequest.DepartmentId);
        Assert.Equal(deps.DefaultDataSeeder.ResultToReturn.LocationId, employeeRequest.LocationId);
        Assert.Equal(deps.DefaultDataSeeder.ResultToReturn.PositionProfileId, employeeRequest.PositionProfileId);
        Assert.Equal(deps.DefaultDataSeeder.ResultToReturn.EmploymentTypeId, employeeRequest.EmploymentTypeId);

        // Supabase Auth user created after the admin employee record.
        var createdUser = Assert.Single(deps.SupabaseAuthGateway.CreatedUsers);
        Assert.Equal(request.AdminEmail, createdUser.Email);
        Assert.EndsWith("/verify-email", createdUser.RedirectTo);

        // No compensation triggered.
        Assert.Empty(deps.Provisioner.DeactivatedCompanyIds);

        // Success audit event published.
        var auditEvent = Assert.Single(deps.AuditEventPublisher.PublishedEvents);
        var registrationEvent = Assert.IsType<RegistrationCreatedAuditEvent>(auditEvent);
        Assert.True(registrationEvent.Succeeded);
        Assert.Equal(result.Value.CompanyId, registrationEvent.CompanyId);
        Assert.Equal(result.Value.UserId, registrationEvent.AdminUserId);
    }

    [Fact]
    public async Task HandleAsync_Creates_UserProfile_With_CompanyAdministrator_And_Employee_Roles()
    {
        var deps = BuildDependencies();
        var supabaseUserId = Guid.NewGuid();
        deps.SupabaseAuthGateway.UserIdToReturn = supabaseUserId;
        var handler = BuildHandler(deps);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db = fixture.BuildContext();
        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.Id == result.Value!.UserId);
        Assert.NotNull(profile);
        Assert.Equal(supabaseUserId, profile!.SupabaseAuthUserId);
        Assert.Equal(result.Value!.CompanyId, profile.CompanyId);
        Assert.Equal(request.AdminEmail, profile.Email);
        Assert.Equal(request.AdminFirstName, profile.FirstName);
        Assert.Equal(request.AdminLastName, profile.LastName);

        // Every seeded persona carries SystemRoles.Employee alongside their specific role — it's
        // the floor role required by "role:employee", which gates core session endpoints
        // (GetMe, GetCompany, etc.) that AppSession depends on for every page.
        //
        // UserRole.UserId must key off the local UserProfile.Id, NOT the raw Supabase auth user
        // id — SupabaseCurrentUserResolutionMiddleware resolves ResolvedCurrentUser.UserId to
        // profile.Id, and every downstream authorization check keys off that. Asserting against
        // the raw Supabase user id explicitly guards against wiring the wrong id here.
        var roles = await db.UserRoles.Where(r => r.UserId == profile.Id).Select(r => r.RoleId).ToListAsync();
        Assert.Contains(SystemRoles.CompanyAdministrator, roles);
        Assert.Contains(SystemRoles.Employee, roles);
        Assert.Equal(2, roles.Count);

        var rolesKeyedToSupabaseId = await db.UserRoles.CountAsync(r => r.UserId == supabaseUserId);
        Assert.Equal(0, rolesKeyedToSupabaseId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_ApplicationUser_For_SelfService_SignUp()
    {
        var deps = BuildDependencies();
        var handler = BuildHandler(deps);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db = fixture.BuildContext();
        var applicationUserExists = await db.Users.AnyAsync(u => u.Email == request.AdminEmail);
        Assert.False(applicationUserExists);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_ApplicationUser_Email_Already_In_Use()
    {
        var existingEmail = $"existing-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(
                Guid.NewGuid(), existingEmail, "hash", "Existing", "User", Now));
            await db.SaveChangesAsync();
        }

        var deps = BuildDependencies();
        var handler = BuildHandler(deps);
        var request = ValidRequest() with { AdminEmail = existingEmail.ToUpperInvariant() };

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(0, deps.Provisioner.CallCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_UserProfile_Email_Already_In_Use()
    {
        var existingEmail = $"existing-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), existingEmail, "Existing", "User", Now));
            await db.SaveChangesAsync();
        }

        var deps = BuildDependencies();
        var handler = BuildHandler(deps);
        var request = ValidRequest() with { AdminEmail = existingEmail.ToUpperInvariant() };

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(0, deps.Provisioner.CallCount);
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

        var deps = BuildDependencies();
        var handler = BuildHandler(deps);
        var request = ValidRequest() with { AdminEmail = existingEmail };

        await handler.HandleAsync(request, CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var userCount = await db2.Users.CountAsync(u => u.Email == existingEmail);
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Company_And_Returns_Failure_When_DefaultDataSeeding_Fails()
    {
        var deps = BuildDependencies();
        deps.DefaultDataSeeder.ShouldThrow = true;
        var handler = BuildHandler(deps);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, deps.Provisioner.CallCount);
        var deactivatedCompanyId = Assert.Single(deps.Provisioner.DeactivatedCompanyIds);
        Assert.NotEqual(Guid.Empty, deactivatedCompanyId);

        var auditEvent = Assert.Single(deps.AuditEventPublisher.PublishedEvents);
        var registrationEvent = Assert.IsType<RegistrationCreatedAuditEvent>(auditEvent);
        Assert.False(registrationEvent.Succeeded);
        Assert.Equal(deactivatedCompanyId, registrationEvent.CompanyId);
        Assert.False(string.IsNullOrWhiteSpace(registrationEvent.FailureReason));
        Assert.Null(registrationEvent.AdminUserId);

        // No admin identity record should have been created since the orchestration failed
        // before reaching the identity-record step.
        Assert.Empty(deps.SupabaseAuthGateway.CreatedUsers);
        await using var db = fixture.BuildContext();
        var userExists = await db.Users.AnyAsync(u => u.Email == request.AdminEmail);
        Assert.False(userExists);
        var profileExists = await db.UserProfiles.AnyAsync(p => p.Email == request.AdminEmail);
        Assert.False(profileExists);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Company_And_Returns_Failure_When_EmployeeProvisioning_Fails()
    {
        var deps = BuildDependencies();
        deps.EmployeeProvisioningService.ShouldFail = true;
        var handler = BuildHandler(deps);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, deps.Provisioner.CallCount);
        Assert.Equal(1, deps.DefaultDataSeeder.CallCount);
        var deactivatedCompanyId = Assert.Single(deps.Provisioner.DeactivatedCompanyIds);
        Assert.NotEqual(Guid.Empty, deactivatedCompanyId);

        var auditEvent = Assert.Single(deps.AuditEventPublisher.PublishedEvents);
        var registrationEvent = Assert.IsType<RegistrationCreatedAuditEvent>(auditEvent);
        Assert.False(registrationEvent.Succeeded);

        await using var db = fixture.BuildContext();
        var userExists = await db.Users.AnyAsync(u => u.Email == request.AdminEmail);
        Assert.False(userExists);
        var profileExists = await db.UserProfiles.AnyAsync(p => p.Email == request.AdminEmail);
        Assert.False(profileExists);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Company_And_Returns_Failure_When_SupabaseAuthGateway_Throws()
    {
        var deps = BuildDependencies();
        deps.SupabaseAuthGateway.ShouldThrowOnCreate = true;
        var handler = BuildHandler(deps);
        var request = ValidRequest();

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, deps.Provisioner.CallCount);
        Assert.Equal(1, deps.DefaultDataSeeder.CallCount);
        Assert.Equal(1, deps.EmployeeProvisioningService.CallCount);
        var deactivatedCompanyId = Assert.Single(deps.Provisioner.DeactivatedCompanyIds);
        Assert.NotEqual(Guid.Empty, deactivatedCompanyId);

        var auditEvent = Assert.Single(deps.AuditEventPublisher.PublishedEvents);
        var registrationEvent = Assert.IsType<RegistrationCreatedAuditEvent>(auditEvent);
        Assert.False(registrationEvent.Succeeded);
        Assert.Equal(deactivatedCompanyId, registrationEvent.CompanyId);
        Assert.Null(registrationEvent.AdminUserId);

        await using var db = fixture.BuildContext();
        var profileExists = await db.UserProfiles.AnyAsync(p => p.Email == request.AdminEmail);
        Assert.False(profileExists);
    }
}
