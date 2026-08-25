namespace HR.Modules.Notifications.Domain;

/// <summary>
/// NOT-03: a single version-controlled (i.e. source-controlled, not runtime-editable) template for
/// one NotificationType. Declares in-app title/body and email subject/body templates, each of which
/// may reference tokens using "{TokenName}" placeholders, plus the declared set of required and
/// optional tokens used both to validate a caller-supplied token dictionary before rendering and
/// (via NotificationTemplateCatalogueTests) to catch a typo'd token placeholder in a template string
/// that doesn't match any declared token.
///
/// Version must be bumped whenever any of the four template strings changes wording, so a support
/// engineer inspecting an EmailDelivery row's TemplateVersion can tell exactly what content a
/// historical email was rendered from.
/// </summary>
internal sealed record NotificationTemplate(
    int Version,
    string InAppTitleTemplate,
    string? InAppBodyTemplate,
    string EmailSubjectTemplate,
    string EmailBodyTemplate,
    IReadOnlySet<string> RequiredTokens,
    IReadOnlySet<string> OptionalTokens);
