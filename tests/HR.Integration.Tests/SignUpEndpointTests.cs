using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SignUpEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SignUpEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static object ValidSignUpRequest(string? email = null) => new
    {
        companyName = $"Acme-{Guid.NewGuid():N}",
        adminFirstName = "Ada",
        adminLastName = "Lovelace",
        adminEmail = email ?? $"ada-{Guid.NewGuid():N}@example.com",
        password = "P@ssw0rd123",
    };

    [Fact]
    public async Task Post_SignUp_Does_Not_Return_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest());

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Creates_Company_And_Admin_User_On_Happy_Path()
    {
        using var client = _factory.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        var supabaseUserId = Guid.NewGuid();
        _factory.SupabaseAuthGateway.UserIdToReturn = supabaseUserId;

        var response = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SignUpPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.UserId);
        Assert.NotEqual(Guid.Empty, payload.CompanyId);
        Assert.Equal(email, payload.Email);
        Assert.Equal("Ada", payload.FirstName);
        Assert.Equal("Lovelace", payload.LastName);

        // The gateway was invoked instead of any real Supabase call, and the resulting
        // UserProfile carries the Supabase auth user id it returned.
        Assert.Contains(_factory.SupabaseAuthGateway.CreatedUsers, u => u.Email == email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.Id == payload.UserId);
        Assert.NotNull(profile);
        Assert.Equal(supabaseUserId, profile!.SupabaseAuthUserId);
        Assert.Equal(payload.CompanyId, profile.CompanyId);
        Assert.Equal(email, profile.Email);

        // Also granted SystemRoles.HrAdministrator alongside CompanyAdministrator — the
        // self-service admin is the company's only user at this point, so CompanyAdministrator
        // alone would lock them out of Employees/HR Settings/User Administration. Plus
        // SystemRoles.Employee, the floor role required by "role:employee", which gates core
        // session endpoints (GetMe, GetCompany, etc.) that every seeded persona also carries (see
        // SignUpHandler remarks). Roles are keyed to UserProfile.Id (payload.UserId), not the raw
        // Supabase auth user id.
        var roleIds = await db.UserRoles.Where(r => r.UserId == payload.UserId).Select(r => r.RoleId).ToListAsync();
        Assert.Contains(SystemRoles.CompanyAdministrator, roleIds);
        Assert.Contains(SystemRoles.HrAdministrator, roleIds);
        Assert.Contains(SystemRoles.Employee, roleIds);
        Assert.Equal(3, roleIds.Count);

        // Self-service signup starts a company in PendingVerification — it is not yet Active
        // until an email-verification step (out of scope for Phase A) flips it.
        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var company = await companiesDb.Companies.SingleOrDefaultAsync(c => c.Id == payload.CompanyId);
        Assert.NotNull(company);
        Assert.Equal(CompanyStatus.PendingVerification, company!.Status);
        Assert.False(company.IsActive);

        // Default setup data (Department/Location/EmploymentType/PositionProfile) was seeded so the
        // admin's own Employee record could be created against it.
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var department = await employeesDb.Departments.SingleOrDefaultAsync(d => d.CompanyId == payload.CompanyId);
        Assert.NotNull(department);
        Assert.Equal("General", department!.Name);

        var location = await employeesDb.Locations.SingleOrDefaultAsync(l => l.CompanyId == payload.CompanyId);
        Assert.NotNull(location);
        Assert.Equal("Head Office", location!.Name);

        // Full default Employment Types set (matches the dev/E2E seed data's canonical set — see
        // CompanyDefaultDataSeeder), not a single placeholder "Full-time" type. "Permanent" is the
        // one actually assigned to the admin's own Employee record below.
        var employmentTypeNames = await employeesDb.EmploymentTypes
            .Where(et => et.CompanyId == payload.CompanyId).Select(et => et.Name).ToListAsync();
        Assert.Equal(
            new[] { "Permanent", "Fixed Term", "Contractor", "Casual", "Apprentice" }.OrderBy(n => n),
            employmentTypeNames.OrderBy(n => n));
        var employmentType = await employeesDb.EmploymentTypes
            .SingleAsync(et => et.CompanyId == payload.CompanyId && et.Name == "Permanent");

        var positionProfile = await employeesDb.PositionProfiles.SingleOrDefaultAsync(pp => pp.CompanyId == payload.CompanyId);
        Assert.NotNull(positionProfile);
        Assert.Equal("Administrator", positionProfile!.Title);
        Assert.Equal(department.Id, positionProfile.DepartmentId);
        Assert.Equal(location.Id, positionProfile.LocationId);

        var employee = await employeesDb.Employees.SingleOrDefaultAsync(e => e.CompanyId == payload.CompanyId);
        Assert.NotNull(employee);
        Assert.Equal(positionProfile.Id, employee!.PositionProfileId);
        Assert.Equal(employmentType.Id, employee.EmploymentTypeId);

        // Also seeded at signup (item 43 — new companies previously got none of these at all):
        // default Leave Types (minus Sick Leave, deliberately removed from the default set),
        // Sickness Categories, and Document Types.
        var leaveDb = scope.ServiceProvider.GetRequiredService<HR.Modules.Leave.Persistence.LeaveDbContext>();
        var leaveTypeNames = await leaveDb.LeaveTypes
            .Where(lt => lt.CompanyId == payload.CompanyId).Select(lt => lt.Name).ToListAsync();
        Assert.Equal(
            new[] { "Annual Leave", "Unpaid Leave", "Compassionate Leave", "Parental Leave", "Time Off In Lieu" }.OrderBy(n => n),
            leaveTypeNames.OrderBy(n => n));

        var sicknessDb = scope.ServiceProvider.GetRequiredService<HR.Modules.Sickness.Persistence.SicknessDbContext>();
        var sicknessCategoryNames = await sicknessDb.SicknessCategories
            .Where(c => c.CompanyId == payload.CompanyId).Select(c => c.Name).ToListAsync();
        Assert.Equal(
            new[] { "Illness", "Injury", "Mental health", "Medical appointment", "Dependant care", "Other" }.OrderBy(n => n),
            sicknessCategoryNames.OrderBy(n => n));

        var documentsDb = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var documentTypeNames = await documentsDb.DocumentTypes
            .Where(dt => dt.CompanyId == payload.CompanyId).Select(dt => dt.Name).ToListAsync();
        Assert.Equal(
            new[] { "Contract", "Passport", "Driving Licence", "Right To Work", "Certificate", "Other" }.OrderBy(n => n),
            documentTypeNames.OrderBy(n => n));
    }

    [Fact]
    public async Task Post_SignUp_Returns_Conflict_For_Duplicate_Email()
    {
        using var client = _factory.CreateClient();
        var email = $"dup-{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Returns_BadRequest_When_CompanyName_Is_Empty()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", new
        {
            companyName = string.Empty,
            adminFirstName = "Ada",
            adminLastName = "Lovelace",
            adminEmail = $"ada-{Guid.NewGuid():N}@example.com",
            password = "P@ssw0rd123",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Returns_BadRequest_When_Email_Is_Invalid()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", new
        {
            companyName = $"Acme-{Guid.NewGuid():N}",
            adminFirstName = "Ada",
            adminLastName = "Lovelace",
            adminEmail = "not-an-email",
            password = "P@ssw0rd123",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Returns_BadRequest_When_Password_Is_Too_Short()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", new
        {
            companyName = $"Acme-{Guid.NewGuid():N}",
            adminFirstName = "Ada",
            adminLastName = "Lovelace",
            adminEmail = $"ada-{Guid.NewGuid():N}@example.com",
            password = "short1",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record SignUpPayload(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);
}
