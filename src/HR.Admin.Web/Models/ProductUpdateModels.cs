namespace HR.Admin.Web.Models;

public sealed record ProductUpdateRecipientPreviewModel(
    int RecipientCount,
    int CompanyCount);

public sealed record SendProductUpdateRequest(
    string Title,
    string Message,
    string? Url);

public sealed record SendProductUpdateResultModel(
    int RecipientCount,
    int CompanyCount);
