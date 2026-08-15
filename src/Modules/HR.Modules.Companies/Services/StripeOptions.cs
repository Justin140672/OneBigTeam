namespace HR.Modules.Companies.Services;

internal sealed class StripeOptions
{
    public string SecretKey { get; set; } = "";
    public string PublishableKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PriceId { get; set; } = "";

    // Displayed on the Admin Portal's Customer List (Monthly Charge column) for customers with an
    // Active paid subscription. Only one plan/price exists today (see PriceId above), so this is a
    // single flat amount rather than a per-price catalogue — revisit if/when multiple plans exist.
    public decimal MonthlyPriceGbp { get; set; }
}
