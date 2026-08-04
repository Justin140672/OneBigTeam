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
        new("30000000-0000-0000-0000-000000000006", Acme,     "Marcus Diallo", "HR Advisor (Recruiter)", "marcus.diallo@acme.example"),
        new("30000000-0000-0000-0000-000000000008", Acme,     "David Park",    "Sales Manager (HR Admin)", "david.park@acme.example"),
        new("30000000-0000-0000-0000-000000000013", Acme,     "Priya Shah",    "Company Administrator", "priya.shah@acme.example"),
        new("30000000-0000-0000-0000-000000000004", Acme,     "Tom Williams",  "Developer",           "tom.williams@acme.example"),
        new("30000000-0000-0000-0000-000000000010", Acme,     "Carlos Rivera", "Account Executive",   "carlos.rivera@acme.example"),
        new("30000000-0000-0000-0000-000000000011", BetaCorp, "Alice Morgan",  "Engineering Manager", "alice.morgan@betacorp.example"),
        new("30000000-0000-0000-0000-000000000012", BetaCorp, "Bob Taylor",    "Software Developer",  "bob.taylor@betacorp.example"),
    ];

    // Personas created via the self-service SignUp flow (HR.Modules.Identity's SignUp feature) —
    // registered here at runtime so the dev-stub auth mechanism can "sign in" a brand-new
    // company/admin without a real Supabase Auth flow. In-memory only (matches the rest of this
    // dev-only stub); lost on API restart.
    private readonly List<DevPersona> _registeredPersonas = [];

    private IEnumerable<DevPersona> AllPersonas => Personas.Concat(_registeredPersonas);

    public DevPersona Current => AllPersonas.First(p => p.UserId == _currentUserId);

    public void Switch(string userId)
    {
        if (AllPersonas.Any(p => p.UserId == userId))
            _currentUserId = userId;
    }

    /// <summary>
    /// Registers a brand-new persona (e.g. a self-service signup admin) and immediately switches
    /// to it, establishing a session the same way the existing persona switcher does.
    /// </summary>
    public void Register(DevPersona persona)
    {
        if (!AllPersonas.Any(p => p.UserId == persona.UserId))
            _registeredPersonas.Add(persona);

        _currentUserId = persona.UserId;
    }
}
