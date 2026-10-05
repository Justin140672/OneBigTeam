namespace HR.Api.Authentication;

public sealed record DevPersona(string UserId, string CompanyId, string Name, string JobTitle, string Email);

public sealed class DevPersonaStore
{
    private const string Acme     = "00000000-0000-0000-0000-000000000001";
    private const string BetaCorp = "00000000-0000-0000-0000-000000000002";
    private const string Gamma    = "00000000-0000-0000-0000-000000000003";

    public static readonly IReadOnlyList<DevPersona> Personas =
    [
        new("30000000-0000-0000-0000-000000000001", Acme,     "Sarah Chen",    "CTO",                 "sarah.chen@acme.example"),
        new("30000000-0000-0000-0000-000000000002", Acme,     "James Okafor",  "Senior Developer",    "james.okafor@acme.example"),
        new("30000000-0000-0000-0000-000000000005", Acme,     "Laura Bennett", "HR Manager",          "laura.bennett@acme.example"),
        new("30000000-0000-0000-0000-000000000006", Acme,     "Marcus Diallo", "HR Advisor (Recruiter)", "marcus.diallo@acme.example"),
        new("30000000-0000-0000-0000-000000000008", Acme,     "David Park",    "Sales Manager (HR Admin)", "david.park@acme.example"),
        new("30000000-0000-0000-0000-000000000013", Acme,     "Priya Shah",    "Company Administrator", "priya.shah@acme.example"),
        new("30000000-0000-0000-0000-000000000014", Acme,     "Justin Etherington", "Company Administrator", "justinetherington@hotmail.com"),
        new("30000000-0000-0000-0000-000000000004", Acme,     "Tom Williams",  "Developer",           "tom.williams@acme.example"),
        new("30000000-0000-0000-0000-000000000010", Acme,     "Carlos Rivera", "Account Executive",   "carlos.rivera@acme.example"),
        new("30000000-0000-0000-0000-000000000011", BetaCorp, "Alice Morgan",  "Engineering Manager", "alice.morgan@betacorp.example"),
        new("30000000-0000-0000-0000-000000000012", BetaCorp, "Bob Taylor",    "Software Developer",  "bob.taylor@betacorp.example"),
        new("30000000-0000-0000-0000-000000000015", BetaCorp, "Grace Kim",     "HR Administrator",    "grace.kim@betacorp.example"),
        new("30000000-0000-0000-0000-000000000018", BetaCorp, "Charlie Wilson", "Company Administrator", "charlie.wilson@betacorp.example"),
        new("30000000-0000-0000-0000-000000000016", Acme,     "Olivia Reyes",  "HR Administrator",    "olivia.reyes@acme.example"),
        new("30000000-0000-0000-0000-000000000017", Acme,     "Nina Patel",    "Team Lead",           "nina.patel@acme.example"),
        new("30000000-0000-0000-0000-000000000020", Acme,     "Ben Carter",    "Developer (Onboarding)", "ben.carter@acme.example"),
        new("30000000-0000-0000-0000-000000000019", Gamma,   "Diana Chen",    "Company Administrator", "diana.chen@gamma.example"),
    ];

    private readonly List<DevPersona> _registeredPersonas = [];

    public IReadOnlyList<DevPersona> RegisteredPersonas => _registeredPersonas;

    public IEnumerable<DevPersona> AllPersonas => Personas.Concat(_registeredPersonas);

    public DevPersona? FindPersona(string userId) =>
        AllPersonas.FirstOrDefault(p => p.UserId == userId);

    public void Register(DevPersona persona)
    {
        if (!AllPersonas.Any(p => p.UserId == persona.UserId))
            _registeredPersonas.Add(persona);
    }
}
