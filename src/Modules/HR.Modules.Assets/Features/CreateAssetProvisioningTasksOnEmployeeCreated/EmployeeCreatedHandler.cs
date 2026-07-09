using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Assets.Features.CreateAssetProvisioningTasksOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler(
    IPositionProfileAssetsReader positionProfileAssetsReader,
    IAssetCategoryReader assetCategoryReader,
    IEmployeeNameReader employeeNameReader,
    ITaskCreator taskCreator) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (e.IsImported)
            return;

        if (e.PositionProfileId is null)
            return;

        var required = await positionProfileAssetsReader.GetActiveAssetsAsync(
            e.CompanyId, e.PositionProfileId.Value, cancellationToken);

        if (required.Count == 0)
            return;

        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(e.EmployeeId, "the new employee");

        var categoryNames = await assetCategoryReader.GetNamesAsync(
            e.CompanyId,
            required.Select(a => a.AssetCategoryId),
            cancellationToken);

        foreach (var requiredAsset in required)
        {
            var categoryName = categoryNames.GetValueOrDefault(requiredAsset.AssetCategoryId, "Asset");

            await taskCreator.CreateAsync(
                e.CompanyId,
                createdBy:          e.EmployeeId,
                title:              $"Provision asset: {categoryName} — {employeeName}",
                description:        $"Assign a {categoryName} to {employeeName} ahead of their start date.",
                priority:           TaskPriority.Medium,
                source:             TaskSource.Asset,
                actionType:         TaskActionType.Complete,
                dueDate:            e.StartDate,
                assignedEmployeeId: null,
                assignedUserId:     null,
                sourceEntityId:     requiredAsset.Id,
                cancellationToken);
        }
    }
}
