namespace HR.Modules.Assets.Domain;

internal sealed class Asset
{
    private Asset() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string AssetNumber { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Manufacturer { get; private set; }
    public string? Model { get; private set; }
    public string? SerialNumber { get; private set; }
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchasePrice { get; private set; }
    public AssetStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Asset Create(
        Guid id,
        Guid companyId,
        string assetNumber,
        Guid categoryId,
        string name,
        string? manufacturer,
        string? model,
        string? serialNumber,
        DateOnly? purchaseDate,
        decimal? purchasePrice,
        DateTimeOffset now)
    {
        return new Asset
        {
            Id = id,
            CompanyId = companyId,
            AssetNumber = assetNumber,
            CategoryId = categoryId,
            Name = name,
            Manufacturer = manufacturer,
            Model = model,
            SerialNumber = serialNumber,
            PurchaseDate = purchaseDate,
            PurchasePrice = purchasePrice,
            Status = AssetStatus.Available,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string assetNumber,
        Guid categoryId,
        string name,
        string? manufacturer,
        string? model,
        string? serialNumber,
        DateOnly? purchaseDate,
        decimal? purchasePrice,
        DateTimeOffset now)
    {
        AssetNumber = assetNumber;
        CategoryId = categoryId;
        Name = name;
        Manufacturer = manufacturer;
        Model = model;
        SerialNumber = serialNumber;
        PurchaseDate = purchaseDate;
        PurchasePrice = purchasePrice;
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
