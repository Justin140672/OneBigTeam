namespace HR.Api.Authentication;

public sealed record DevPersona(string UserId, string CompanyId, string Name, string JobTitle, string Email);

public sealed class DevPersonaStore
{
    private volatile string _currentUserId = "30000000-0000-0000-0000-000000000001";

    private const string Acme     = "00000000-0000-0000-0000-000000000001";
    private const string BetaCorp = "00000000-0000-0000-0000-000000000002";

    public static readonly IReadOnlyList<DevPersona> Personas =
    [
        new("30000000-0000-0000-0000-000000000001", Acme,     "Sarah Chen",    "CTO",                 "sarah.chen@acme.example"),
        new("30000000-0000-0000-0000-000000000002", Acme,     "James Okafor",  "Senior Developer",    "james.okafor@acme.example"),
        new("30000000-0000-0000-0000-000000000005", Acme,     "Laura Bennett", "HR Manager",          "laura.bennett@acme.example"),
        new("30000000-0000-0000-0000-000000000013", Acme,     "Priya Shah",    "Company Administrator", "priya.shah@acme.example"),
        new("30000000-0000-0000-0000-000000000004", Acme,     "Tom Williams",  "Developer",           "tom.williams@acme.example"),
        new("30000000-0000-0000-0000-000000000010", Acme,     "Carlos Rivera", "Account Executive",   "carlos.rivera@acme.example"),
        new("30000000-0000-0000-0000-000000000011", BetaCorp, "Alice Morgan",  "Engineering Manager", "alice.morgan@betacorp.example"),
        new("30000000-0000-0000-0000-000000000012", BetaCorp, "Bob Taylor",    "Software Developer",  "bob.taylor@betacorp.example"),
    ];

    public DevPersona Current => Personas.First(p => p.UserId == _currentUserId);

    public void Switch(string userId)
    {
        if (Personas.Any(p => p.UserId == userId))
            _currentUserId = userId;
    }
}
