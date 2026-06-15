namespace HR.Api.Authentication;

public sealed record DevPersona(string UserId, string Name, string JobTitle, string Email);

public sealed class DevPersonaStore
{
    private volatile string _currentUserId = "30000000-0000-0000-0000-000000000001";

    public static readonly IReadOnlyList<DevPersona> Personas =
    [
        new("30000000-0000-0000-0000-000000000001", "Sarah Chen",    "CTO",              "sarah.chen@acme.example"),
        new("30000000-0000-0000-0000-000000000002", "James Okafor",  "Senior Developer", "james.okafor@acme.example"),
        new("30000000-0000-0000-0000-000000000005", "Laura Bennett", "HR Manager",       "laura.bennett@acme.example"),
        new("30000000-0000-0000-0000-000000000004", "Tom Williams",  "Developer",        "tom.williams@acme.example"),
    ];

    public DevPersona Current => Personas.First(p => p.UserId == _currentUserId);

    public void Switch(string userId)
    {
        if (Personas.Any(p => p.UserId == userId))
            _currentUserId = userId;
    }
}
