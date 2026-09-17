using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class LeaveTypeServiceTests
{
    private static LeaveTypeEditModel SampleModel() => new() { Name = "Sick Leave", Code = "SICK" };

    // ── UpdateAsync(ApiSaveResult) ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateLeaveTypeResponse(Guid.NewGuid(), Guid.NewGuid(), "Sick Leave", "SICK", 10, "None", "Standard", true, true, false, DateTimeOffset.UtcNow, Version: 2);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new LeaveTypeService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_When_Api_Returns_Conflict_With_Concurrency_Code()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Changed by someone else.", code = "concurrency" }));
        var service = new LeaveTypeService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Does_Not_Flag_ConcurrencyConflict_For_Plain_Business_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "A leave type with this code already exists." }));
        var service = new LeaveTypeService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new LeaveTypeService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("Your session has expired. Please sign in again.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new LeaveTypeService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new LeaveTypeService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "Code is required." }));
        var service = new LeaveTypeService(factory);

        var (result, error) = await service.CreateAsync(Guid.NewGuid(), new CreateLeaveTypeRequest(Guid.NewGuid(), "Sick Leave", "", 10, "None", "Standard"));

        Assert.Null(result);
        Assert.Equal("Code is required.", error);
    }

    // ── DeactivateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_Returns_Null_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new LeaveTypeService(factory);

        var error = await service.DeactivateAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(error);
    }

    [Fact]
    public async Task DeactivateAsync_Returns_Error_When_Api_Returns_Conflict_For_System_Type()
    {
        // Guards against deactivating the platform-provisioned "Annual Leave" system type.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "System leave types cannot be deactivated." }));
        var service = new LeaveTypeService(factory);

        var error = await service.DeactivateAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("System leave types cannot be deactivated.", error);
    }

    // ── ListLeaveTypesAsync (representative read) ────────────────────────────────

    [Fact]
    public async Task ListLeaveTypesAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new ListLeaveTypesResponse([]);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new LeaveTypeService(factory);

        var result = await service.ListLeaveTypesAsync(Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListLeaveTypesAsync_Returns_Null_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new LeaveTypeService(factory);

        var result = await service.ListLeaveTypesAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
