namespace HR.Admin.Web.Models;

/// <summary>Mirrors PreviewProductUpdateRecipientsResponse (HR.Modules.Notifications).</summary>
public sealed record ProductUpdateRecipientPreviewModel(
    int RecipientCount,
    int CompanyCount);

/// <summary>Mirrors SendProductUpdateRequest.</summary>
public sealed record SendProductUpdateRequest(
    string Title,
    string Message,
    string? Url);

/// <summary>Mirrors SendProductUpdateResponse.</summary>
public sealed record SendProductUpdateResultModel(
    int RecipientCount,
    int CompanyCount);
