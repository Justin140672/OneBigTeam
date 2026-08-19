namespace HR.Modules.Companies.Contracts;

public sealed record CompanyContactValidationRules(
    string PostcodeRegex,
    string TelephoneRegex,
    string MobileRegex);
