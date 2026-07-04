namespace HR.Infrastructure.Abstractions;

public sealed record CompanyContactValidationRules(
    string PostcodeRegex,
    string TelephoneRegex,
    string MobileRegex);
