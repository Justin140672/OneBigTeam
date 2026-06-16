namespace HR.Modules.Employees.Domain;

internal sealed class Nationality
{
    private Nationality() { }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public static Nationality Create(int id, string name) => new() { Id = id, Name = name };
}
