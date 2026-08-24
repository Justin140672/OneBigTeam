namespace HR.Modules.Companies.Contracts;

public enum AssetNumberMode
{
    // Default: every asset number is entered manually, mirroring EmployeeNumberMode's default
    // rationale — no new generation logic is required and it matches pre-existing behaviour where
    // every asset number was manually typed.
    Manual = 0,
    Automatic = 1,
}
