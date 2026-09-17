using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class UserAdministrationServiceTests
{
    // ── ListUsersAsync (representative read) ─────────────────────────────────────

    [Fact]
    public async Task ListUsersAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new ListUsersResponse([], 0, 1, 100);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new UserAdministrationService(factory);

        var result = await service.ListUsersAsync(Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListUsersAsync_Returns_Null_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new UserAdministrationService(factory);

        var result = await service.ListUsersAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListUsersAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new UserAdministrationService(factory);

        var result = await service.ListUsersAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // ── InviteEmployeeUserAsync (write) ──────────────────────────────────────────

    [Fact]
    public async Task InviteEmployeeUserAsync_Returns_Result_When_Api_Returns_Created()
    {
        var response = new InviteEmployeeUserResponse(Guid.NewGuid(), Guid.NewGuid(), "jane@example.com", DateTimeOffset.UtcNow.AddDays(7));
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, response));
        var service = new UserAdministrationService(factory);

        var (result, error) = await service.InviteEmployeeUserAsync(Guid.NewGuid(), Guid.NewGuid(), "jane@example.com", [SystemRoleOptions.EmployeeRoleId]);

        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task InviteEmployeeUserAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "'not-an-email' is not a valid email address." }));
        var service = new UserAdministrationService(factory);

        var (result, error) = await service.InviteEmployeeUserAsync(Guid.NewGuid(), Guid.NewGuid(), "not-an-email", [SystemRoleOptions.EmployeeRoleId]);

        Assert.Null(result);
        Assert.Equal("'not-an-email' is not a valid email address.", error);
    }

    [Fact]
    public async Task InviteEmployeeUserAsync_Returns_Failure_When_Api_Returns_Conflict_For_Already_Invited_Employee()
    {
        // Guards against re-inviting an employee that already has a pending invite/account.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "This employee already has a user account or pending invite." }));
        var service = new UserAdministrationService(factory);

        var (result, error) = await service.InviteEmployeeUserAsync(Guid.NewGuid(), Guid.NewGuid(), "jane@example.com", [SystemRoleOptions.EmployeeRoleId]);

        Assert.Null(result);
        Assert.Equal("This employee already has a user account or pending invite.", error);
    }

    [Fact]
    public async Task InviteEmployeeUserAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler(HttpStatusCode.Created));
        var service = new UserAdministrationService(factory);

        var (result, error) = await service.InviteEmployeeUserAsync(Guid.NewGuid(), Guid.NewGuid(), "jane@example.com", [SystemRoleOptions.EmployeeRoleId]);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ── UpdateUserRolesAsync (write, no-content) ─────────────────────────────────

    [Fact]
    public async Task UpdateUserRolesAsync_Returns_Success_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new UserAdministrationService(factory);

        var (success, error) = await service.UpdateUserRolesAsync(Guid.NewGuid(), Guid.NewGuid(), [SystemRoleOptions.EmployeeRoleId]);

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new UserAdministrationService(factory);

        var (success, error) = await service.UpdateUserRolesAsync(Guid.NewGuid(), Guid.NewGuid(), [SystemRoleOptions.EmployeeRoleId]);

        Assert.False(success);
        Assert.Equal("You do not have permission to perform this action.", error);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_Returns_Failure_When_Api_Returns_NotFound()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "User not found." }));
        var service = new UserAdministrationService(factory);

        var (success, error) = await service.UpdateUserRolesAsync(Guid.NewGuid(), Guid.NewGuid(), [SystemRoleOptions.EmployeeRoleId]);

        Assert.False(success);
        Assert.Equal("User not found.", error);
    }

    // ── DisableUserAsync / EnableUserAsync ────────────────────────────────────────

    [Fact]
    public async Task DisableUserAsync_Returns_Success_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new UserAdministrationService(factory);

        var (success, error) = await service.DisableUserAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public async Task DisableUserAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new UserAdministrationService(factory);

        var (success, error) = await service.DisableUserAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("Your session has expired. Please sign in again.", error);
    }
}
