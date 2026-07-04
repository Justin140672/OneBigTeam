namespace HR.Modules.Companies.Domain;

// Default validation patterns seeded onto a company's settings when it's created. Not surfaced
// or editable via the Company Settings UI today — they exist so Employees/Companies validation
// has a per-company pattern to check against, with these UK formats as the out-of-the-box default.
internal static class UkContactRegexDefaults
{
    public const string Postcode = @"^[A-Za-z]{1,2}\d[A-Za-z\d]?\s?\d[A-Za-z]{2}$";
    public const string Telephone = @"^(?:\+44\s?|0)(?:\d\s?){9,10}$";
    public const string Mobile = @"^(?:\+44\s?|0)7\d{3}(?:\s?\d{3}){2}$";
}
