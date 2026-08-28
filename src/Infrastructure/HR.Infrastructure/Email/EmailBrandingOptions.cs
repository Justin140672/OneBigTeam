namespace HR.Infrastructure.Email;

internal sealed class EmailBrandingOptions
{
    public string ProductName { get; set; } = "One Big Team";
    public string? LogoUrl { get; set; }
    public string? SupportEmail { get; set; }
    public string CompanyName { get; set; } = "One Big Team";
    public string? CompanyAddress { get; set; }
}
