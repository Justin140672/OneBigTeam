namespace HR.Api.Authentication;

public sealed record DevPersona(string UserId, string CompanyId, string Name, string JobTitle, string Email);

/// <summary>
/// Catalog of dev-only seed personas used to populate the persona switcher and to seed matching
/// Supabase Auth users in Development (see IdentityModule.SeedDevSupabaseUsersAsync). Development
/// now always authenticates through real Supabase (see the "Switch development to real Supabase
/// auth" plan) — this store is no longer a claims source, only a catalog/registry.
/// </summary>
public sealed class DevPersonaStore
{
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
        new("30000000-0000-0000-0000-000000000014", Acme,     "Justin Etherington", "Company Administrator", "justinetherington@hotmail.com"),
        new("30000000-0000-0000-0000-000000000004", Acme,     "Tom Williams",  "Developer",           "tom.williams@acme.example"),
        new("30000000-0000-0000-0000-000000000010", Acme,     "Carlos Rivera", "Account Executive",   "carlos.rivera@acme.example"),
        new("30000000-0000-0000-0000-000000000011", BetaCorp, "Alice Morgan",  "Engineering Manager", "alice.morgan@betacorp.example"),
        new("30000000-0000-0000-0000-000000000012", BetaCorp, "Bob Taylor",    "Software Developer",  "bob.taylor@betacorp.example"),
    ];

    // Personas created via the self-service SignUp flow (HR.Modules.Identity's SignUp feature) —
    // registered here at runtime so /api/dev/persona/register and the persona switcher can find a
    // brand-new company/admin's email for the real Supabase password-grant login. In-memory only
    // (matches the rest of this dev-only stub); lost on API restart.
    private readonly List<DevPersona> _registeredPersonas = [];

    public IReadOnlyList<DevPersona> RegisteredPersonas => _registeredPersonas;

    public IEnumerable<DevPersona> AllPersonas => Personas.Concat(_registeredPersonas);

    public DevPersona? FindPersona(string userId) =>
        AllPersonas.FirstOrDefault(p => p.UserId == userId);

    /// <summary>
    /// Registers a brand-new persona (e.g. a self-service signup admin) so it can be looked up by
    /// the persona switcher / register endpoint for its real Supabase login.
    /// </summary>
    public void Register(DevPersona persona)
    {
        if (!AllPersonas.Any(p => p.UserId == persona.UserId))
            _registeredPersonas.Add(persona);
    }
}
