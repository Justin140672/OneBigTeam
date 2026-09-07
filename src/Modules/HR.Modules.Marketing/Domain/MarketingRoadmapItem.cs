using HR.SharedKernel;

namespace HR.Modules.Marketing.Domain;

/// <summary>
/// A roadmap / "coming soon" item shown on the marketing site. Global/system content — see
/// <see cref="MarketingProduct"/> for the company_id exception rationale.
/// </summary>
internal sealed class MarketingRoadmapItem
{
    private MarketingRoadmapItem() { }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string IconName { get; private set; } = string.Empty;
    public MarketingDeliveryStatus DeliveryStatus { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsPublished { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    public static Result<MarketingRoadmapItem> Create(
        Guid id,
        Guid productId,
        string title,
        string description,
        string iconName,
        MarketingDeliveryStatus deliveryStatus,
        int displayOrder,
        Guid? createdByUserId,
        DateTimeOffset now)
    {
        var validation = Validate(title, description, displayOrder);
        if (validation.IsFailure)
        {
            return Result.Failure<MarketingRoadmapItem>(validation.Error);
        }

        return Result.Success(new MarketingRoadmapItem
        {
            Id = id,
            ProductId = productId,
            Title = title.Trim(),
            Description = description.Trim(),
            IconName = (iconName ?? string.Empty).Trim(),
            DeliveryStatus = deliveryStatus,
            DisplayOrder = displayOrder,
            IsPublished = false,
            CreatedAt = now,
            CreatedByUserId = createdByUserId,
            UpdatedAt = now,
            UpdatedByUserId = createdByUserId,
        });
    }

    public Result Update(
        string title,
        string description,
        string iconName,
        MarketingDeliveryStatus deliveryStatus,
        int displayOrder,
        Guid? updatedByUserId,
        DateTimeOffset now)
    {
        var validation = Validate(title, description, displayOrder);
        if (validation.IsFailure)
        {
            return validation;
        }

        Title = title.Trim();
        Description = description.Trim();
        IconName = (iconName ?? string.Empty).Trim();
        DeliveryStatus = deliveryStatus;
        DisplayOrder = displayOrder;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = now;
        return Result.Success();
    }

    public void Publish(Guid? userId, DateTimeOffset now)
    {
        IsPublished = true;
        UpdatedByUserId = userId;
        UpdatedAt = now;
    }

    public void Unpublish(Guid? userId, DateTimeOffset now)
    {
        IsPublished = false;
        UpdatedByUserId = userId;
        UpdatedAt = now;
    }

    public void SetDisplayOrder(int displayOrder, Guid? userId, DateTimeOffset now)
    {
        DisplayOrder = displayOrder;
        UpdatedByUserId = userId;
        UpdatedAt = now;
    }

    private static Result Validate(string title, string description, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure(Error.Validation("Title is required."));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure(Error.Validation("Description is required."));
        }

        if (displayOrder < 0)
        {
            return Result.Failure(Error.Validation("Display order must be zero or greater."));
        }

        return Result.Success();
    }
}
