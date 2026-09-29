namespace HR.Modules.Identity.Services;

internal sealed class EmailAlreadyRegisteredException(string email)
    : Exception($"An account with this email already exists.")
{
    public string Email { get; } = email;
}
