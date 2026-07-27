namespace HR.Infrastructure.Abstractions;

public enum EmployeeNumberMode
{
    // Default: every employee number is entered manually. Kept as the default because it
    // requires no new generation logic and matches the pre-existing behaviour where every
    // employee number was manually typed.
    Manual = 0,
    Automatic = 1,
}
