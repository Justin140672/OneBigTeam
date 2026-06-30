namespace HR.Modules.Assets.Domain;

internal sealed class Asset
{
    private Asset() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string AssetTag { get; private set; } = string.Empty;
    public Guid AssetCategoryId { get; private set; }
    public AssetStatus Status { get; private set; }
    public string? SerialNumber { get; private set; }
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchasePrice { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Asset Create(
        Guid id,
        Guid companyId,
        string name,
        string assetTag,
        Guid assetCategoryId,
        string? serialNumber,
        DateOnly? purchaseDate,
        decimal? purchasePrice,
        string? notes,
        DateTimeOffset now)
    {
        return new Asset
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            AssetTag = assetTag,
            AssetCategoryId = assetCategoryId,
            Status = AssetStatus.Available,
            SerialNumber = serialNumber,
            PurchaseDate = purchaseDate,
            PurchasePrice = purchasePrice,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string name,
        string assetTag,
        Guid assetCategoryId,
        string? serialNumber,
        DateOnly? purchaseDate,
        decimal? purchasePrice,
        string? notes,
        DateTimeOffset now)
    {
        Name = name;
        AssetTag = assetTag;
        AssetCategoryId = assetCategoryId;
        SerialNumber = serialNumber;
        PurchaseDate = purchaseDate;
        PurchasePrice = purchasePrice;
        Notes = notes;
        UpdatedAt = now;
    }

    public void MarkAssigned(DateTimeOffset now)
    {
        Status = AssetStatus.Assigned;
        UpdatedAt = now;
    }

    public void MarkAvailable(DateTimeOffset now)
    {
        Status = AssetStatus.Available;
        UpdatedAt = now;
    }

    public void MarkUnderRepair(DateTimeOffset now)
    {
        Status = AssetStatus.UnderRepair;
        UpdatedAt = now;
    }

    public void Retire(DateTimeOffset now)
    {
        if (Status == AssetStatus.Assigned)
            throw new InvalidOperationException("Cannot retire an asset that is currently assigned.");

        Status = AssetStatus.Retired;
        UpdatedAt = now;
    }
}
