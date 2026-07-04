namespace HR.Modules.Employees.Tests.Infrastructure;

// Mirrors HR.Modules.Companies.Domain.UkContactRegexDefaults (internal to that module, so not
// directly referenceable here). Kept as literal patterns so these tests exercise the same
// contract a real company's settings would apply, independent of Companies-module internals.
internal static class UkTestRegexPatterns
{
    public const string Postcode = @"^[A-Za-z]{1,2}\d[A-Za-z\d]?\s?\d[A-Za-z]{2}$";
    public const string Telephone = @"^(?:\+44\s?|0)(?:\d\s?){9,10}$";
    public const string Mobile = @"^(?:\+44\s?|0)7\d{3}(?:\s?\d{3}){2}$";
}
