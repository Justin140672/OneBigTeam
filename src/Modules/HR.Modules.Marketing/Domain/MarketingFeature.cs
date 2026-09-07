using System.Text.Json;
using System.Text.RegularExpressions;

using HR.SharedKernel;

namespace HR.Modules.Marketing.Domain;

/// <summary>
/// A single marketed product feature ("Employee Management", "Leave Management", ...). Global/system
/// content — see <see cref="MarketingProduct"/> for the company_id exception rationale.
/// </summary>
internal sealed class MarketingFeature
{
    internal static readonly Regex SlugPattern = new(
        "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions BenefitsJsonOptions = new(JsonSerializerDefaults.Web);

    private MarketingFeature() { }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string IconName { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string Intro { get; private set; } = string.Empty;
    public string? DetailedContent { get; private set; }

    /// <summary>Serialized <see cref="IReadOnlyList{T}"/> of benefit strings, stored as jsonb.</summary>
    public string BenefitsJson { get; private set; } = "[]";

    public string? YouTubeId { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsPublished { get; private set; }
    public MarketingDeliveryStatus DeliveryStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    /// <summary>De/serializes <see cref="BenefitsJson"/>. Not mapped by EF (see configuration).</summary>
    public IReadOnlyList<string> Benefits
    {
        get => JsonSerializer.Deserialize<List<string>>(
            string.IsNullOrWhiteSpace(BenefitsJson) ? "[]" : BenefitsJson, BenefitsJsonOptions) ?? [];
        private set => BenefitsJson = JsonSerializer.Serialize(value ?? [], BenefitsJsonOptions);
    }

    public static Result<MarketingFeature> Create(
        Guid id,
        Guid productId,
        string slug,
        string iconName,
        string title,
        string summary,
        string intro,
        string? detailedContent,
        IReadOnlyList<string> benefits,
        string? youTubeId,
        int displayOrder,
        MarketingDeliveryStatus deliveryStatus,
        Guid? createdByUserId,
        DateTimeOffset now)
    {
        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        var validation = Validate(normalizedSlug, title, summary, displayOrder);
        if (validation.IsFailure)
        {
            return Result.Failure<MarketingFeature>(validation.Error);
        }

        var feature = new MarketingFeature
        {
            Id = id,
            ProductId = productId,
            Slug = normalizedSlug,
            IconName = (iconName ?? string.Empty).Trim(),
            Title = title.Trim(),
            Summary = summary.Trim(),
            Intro = (intro ?? string.Empty).Trim(),
            DetailedContent = string.IsNullOrWhiteSpace(detailedContent) ? null : detailedContent,
            YouTubeId = string.IsNullOrWhiteSpace(youTubeId) ? null : youTubeId.Trim(),
            DisplayOrder = displayOrder,
            IsPublished = false,
            DeliveryStatus = deliveryStatus,
            CreatedAt = now,
            CreatedByUserId = createdByUserId,
            UpdatedAt = now,
            UpdatedByUserId = createdByUserId,
        };
        feature.Benefits = benefits ?? [];
        return Result.Success(feature);
    }

    public Result Update(
        string slug,
        string iconName,
        string title,
        string summary,
        string intro,
        string? detailedContent,
        IReadOnlyList<string> benefits,
        string? youTubeId,
        int displayOrder,
        MarketingDeliveryStatus deliveryStatus,
        Guid? updatedByUserId,
        DateTimeOffset now)
    {
        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        var validation = Validate(normalizedSlug, title, summary, displayOrder);
        if (validation.IsFailure)
        {
            return validation;
        }

        Slug = normalizedSlug;
        IconName = (iconName ?? string.Empty).Trim();
        Title = title.Trim();
        Summary = summary.Trim();
        Intro = (intro ?? string.Empty).Trim();
        DetailedContent = string.IsNullOrWhiteSpace(detailedContent) ? null : detailedContent;
        Benefits = benefits ?? [];
        YouTubeId = string.IsNullOrWhiteSpace(youTubeId) ? null : youTubeId.Trim();
        DisplayOrder = displayOrder;
        DeliveryStatus = deliveryStatus;
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

    private static Result Validate(string slug, string title, string summary, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result.Failure(Error.Validation("Slug is required."));
        }

        if (!SlugPattern.IsMatch(slug))
        {
            return Result.Failure(Error.Validation(
                "Slug must be lower-case alphanumeric words separated by single hyphens."));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure(Error.Validation("Title is required."));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return Result.Failure(Error.Validation("Summary is required."));
        }

        if (displayOrder < 0)
        {
            return Result.Failure(Error.Validation("Display order must be zero or greater."));
        }

        return Result.Success();
    }
}
