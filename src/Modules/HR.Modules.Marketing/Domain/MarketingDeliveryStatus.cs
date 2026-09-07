namespace HR.Modules.Marketing.Domain;

/// <summary>
/// Delivery state of a marketing content item. Deliberately independent of the item's publication
/// state (<c>IsPublished</c>): a feature can be published while still "Coming Soon", and an
/// unpublished feature can already be "Available".
/// </summary>
internal enum MarketingDeliveryStatus
{
    Available,
    ComingSoon,
    Planned,
    Exploring,
}
