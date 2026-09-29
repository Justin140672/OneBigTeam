using System.Net;
using HR.Web.Models;
using HR.Web.Services;

namespace HR.Web.Tests;

public class TaskServiceTests
{
    private static TaskDetailModel SampleTask(Guid companyId, Guid taskId) => new(
        taskId, companyId, "Complete return to work review", "Description", "Open", "High",
        "Sickness", "Review", DateOnly.FromDateTime(DateTime.UtcNow), null, null, Guid.NewGuid(),
        Guid.NewGuid(), null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetTaskResultAsync_Returns_Loaded_With_Task_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var task = SampleTask(companyId, taskId);

        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.JsonResponseHandler(HttpStatusCode.OK, task));
        var service = new TaskService(factory);

        var result = await service.GetTaskResultAsync(companyId, taskId);

        Assert.Equal(TaskLoadStatus.Loaded, result.Status);
        Assert.NotNull(result.Task);
        Assert.Equal(taskId, result.Task!.Id);
    }

    [Fact]
    public async Task GetTaskResultAsync_Returns_NotFound_On_404()
    {
        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.JsonResponseHandler(HttpStatusCode.NotFound, null));
        var service = new TaskService(factory);

        var result = await service.GetTaskResultAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(TaskLoadStatus.NotFound, result.Status);
        Assert.Null(result.Task);
    }

    [Fact]
    public async Task GetTaskResultAsync_Returns_NotFound_On_403_Forbidden()
    {
        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new TaskService(factory);

        var result = await service.GetTaskResultAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(TaskLoadStatus.NotFound, result.Status);
        Assert.Null(result.Task);
    }

    [Fact]
    public async Task GetTaskResultAsync_Returns_Failed_On_500_ServerError()
    {
        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.JsonResponseHandler(HttpStatusCode.InternalServerError, null));
        var service = new TaskService(factory);

        var result = await service.GetTaskResultAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(TaskLoadStatus.Failed, result.Status);
        Assert.Null(result.Task);
    }

    [Fact]
    public async Task GetTaskResultAsync_Returns_Failed_On_Network_Exception()
    {
        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.ThrowingHandler());
        var service = new TaskService(factory);

        var result = await service.GetTaskResultAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(TaskLoadStatus.Failed, result.Status);
        Assert.Null(result.Task);
    }

    [Fact]
    public async Task GetTaskResultAsync_Returns_Failed_On_Malformed_Json_Body()
    {
        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.MalformedJsonHandler());
        var service = new TaskService(factory);

        var result = await service.GetTaskResultAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(TaskLoadStatus.Failed, result.Status);
        Assert.Null(result.Task);
    }

    [Fact]
    public async Task GetTaskAsync_Delegates_To_GetTaskResultAsync_And_Returns_Task_When_Loaded()
    {
        var companyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var task = SampleTask(companyId, taskId);

        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.JsonResponseHandler(HttpStatusCode.OK, task));
        var service = new TaskService(factory);

        var result = await service.GetTaskAsync(companyId, taskId);

        Assert.NotNull(result);
        Assert.Equal(taskId, result!.Id);
    }

    [Fact]
    public async Task GetTaskAsync_Returns_Null_When_NotFound()
    {
        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.JsonResponseHandler(HttpStatusCode.NotFound, null));
        var service = new TaskService(factory);

        var result = await service.GetTaskAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetTaskAsync_Returns_Null_When_Failed()
    {
        var factory = ApiTestSupport.BuildFactory(new ApiTestSupport.ThrowingHandler());
        var service = new TaskService(factory);

        var result = await service.GetTaskAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }
}
