using HR.Modules.Assets.Features.CreateAssetProvisioningTasksOnEmployeeCreated;
using HR.Modules.Assets.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Assets.Tests;

public class CreateAssetProvisioningTasksOnEmployeeCreatedHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static (EmployeeCreatedHandler Handler, FakeTaskCreator TaskCreator) BuildHandler(
        IReadOnlyList<PositionProfileRequiredAssetItem> requiredAssets,
        IReadOnlyDictionary<Guid, string>? categoryNames = null,
        Dictionary<Guid, string>? employeeNames = null)
    {
        var taskCreator = new FakeTaskCreator();
        var handler = new EmployeeCreatedHandler(
            new FakePositionProfileAssetsReader(requiredAssets),
            new FakeAssetCategoryReader(categoryNames ?? new Dictionary<Guid, string>()),
            new FakeEmployeeNameReader(employeeNames),
            taskCreator);
        return (handler, taskCreator);
    }

    private static EmployeeCreatedIntegrationEvent MakeEvent(
        Guid companyId,
        Guid employeeId,
        Guid? positionProfileId = null) =>
        new(companyId, employeeId, StartDate, null, new DateOnly(2027, 1, 1), positionProfileId);

    private static PositionProfileRequiredAssetItem MakeAsset(
        Guid assetCategoryId,
        bool isMandatory = true,
        int quantity = 1,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), assetCategoryId, isMandatory, quantity);

    [Fact]
    public async Task HandleAsync_Creates_One_Task_Per_Required_Asset_Category()
    {
        var categoryId1 = Guid.NewGuid();
        var categoryId2 = Guid.NewGuid();
        var categoryNames = new Dictionary<Guid, string> { [categoryId1] = "Laptop", [categoryId2] = "Monitor" };

        var (handler, taskCreator) = BuildHandler(
            [MakeAsset(categoryId1), MakeAsset(categoryId2)],
            categoryNames);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, taskCreator.Created.Count);
    }

    [Fact]
    public async Task HandleAsync_Skips_When_PositionProfileId_Is_Null()
    {
        var (handler, taskCreator) = BuildHandler([]);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), positionProfileId: null), CancellationToken.None);

        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Skips_When_No_Required_Assets_Configured()
    {
        var (handler, taskCreator) = BuildHandler([]);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Task_Source_Is_Asset_And_ActionType_Is_Complete()
    {
        var categoryId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(
            [MakeAsset(categoryId)],
            new Dictionary<Guid, string> { [categoryId] = "Laptop" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(TaskSource.Asset, task.Source);
        Assert.Equal(TaskActionType.Complete, task.ActionType);
    }

    [Fact]
    public async Task HandleAsync_Task_Title_Includes_CategoryName_And_EmployeeName()
    {
        var employeeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(
            [MakeAsset(categoryId)],
            new Dictionary<Guid, string> { [categoryId] = "Laptop" },
            new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), employeeId, Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal("Provision asset: Laptop — Jane Doe", task.Title);
        Assert.Equal("Assign a Laptop to Jane Doe ahead of their start date.", task.Description);
    }

    [Fact]
    public async Task HandleAsync_Task_Uses_Fallback_EmployeeName_When_Not_Found()
    {
        var categoryId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(
            [MakeAsset(categoryId)],
            new Dictionary<Guid, string> { [categoryId] = "Laptop" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Contains("the new employee", task.Title);
    }

    [Fact]
    public async Task HandleAsync_Task_DueDate_Matches_StartDate()
    {
        var categoryId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(
            [MakeAsset(categoryId)],
            new Dictionary<Guid, string> { [categoryId] = "Laptop" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(StartDate, task.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Task_Is_Not_Assigned_To_A_Specific_Employee_Or_User()
    {
        var categoryId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(
            [MakeAsset(categoryId)],
            new Dictionary<Guid, string> { [categoryId] = "Laptop" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Null(task.AssignedEmployeeId);
        Assert.Null(task.AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Task_SourceEntityId_Is_RequiredAssetId()
    {
        var categoryId = Guid.NewGuid();
        var requiredAssetId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(
            [MakeAsset(categoryId, id: requiredAssetId)],
            new Dictionary<Guid, string> { [categoryId] = "Laptop" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(requiredAssetId, task.SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_Uses_Fallback_CategoryName_When_Not_Found()
    {
        var categoryId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler([MakeAsset(categoryId)]);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Contains("Provision asset: Asset", task.Title);
    }
}
