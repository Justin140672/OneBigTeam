namespace HR.Modules.Companies.Domain;

internal static class UkContactRegexDefaults
{
    public const string Postcode = @"^[A-Za-z]{1,2}\d[A-Za-z\d]?\s?\d[A-Za-z]{2}$";
    public const string Telephone = @"^(?:\+44\s?|0)(?:\d\s?){9,10}$";
    public const string Mobile = @"^(?:\+44\s?|0)7\d{3}(?:\s?\d{3}){2}$";
}
