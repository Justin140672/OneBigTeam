namespace HR.Modules.Employees.Domain;

internal sealed class PositionProfileRequiredAsset
{
    private PositionProfileRequiredAsset() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PositionProfileId { get; private set; }
    public Guid AssetCategoryId { get; private set; }
    public bool IsMandatory { get; private set; }
    public int Quantity { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }

    public static PositionProfileRequiredAsset Create(
        Guid id,
        Guid companyId,
        Guid positionProfileId,
        Guid assetCategoryId,
        bool isMandatory,
        int quantity,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new PositionProfileRequiredAsset
        {
            Id = id,
            CompanyId = companyId,
            PositionProfileId = positionProfileId,
            AssetCategoryId = assetCategoryId,
            IsMandatory = isMandatory,
            Quantity = quantity,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
