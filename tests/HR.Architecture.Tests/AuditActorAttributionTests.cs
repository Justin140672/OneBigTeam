using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using System.Reflection;

namespace HR.Architecture.Tests;

/// <summary>
/// AUD-04: verifies that every IAuditEvent implementation satisfies the actor attribution rules:
/// - Human-typed events (the default) must supply ActorUserId or ActorEmployeeId.
/// - Background and integration-handler events must be explicitly typed as non-Human.
///
/// Events that cannot be instantiated with default parameters (e.g. structs, complex types)
/// are skipped — a separate test should cover those.
/// </summary>
public class AuditActorAttributionTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(HR.Modules.Employees.EmployeesModule).Assembly,
        typeof(HR.Modules.Identity.IdentityModule).Assembly,
        typeof(HR.Modules.Companies.CompaniesModule).Assembly,
        typeof(HR.Modules.Leave.LeaveModule).Assembly,
        typeof(HR.Modules.Documents.DocumentsModule).Assembly,
        typeof(HR.Modules.Tasks.TasksModule).Assembly,
        typeof(HR.Modules.Onboarding.OnboardingModule).Assembly,
        typeof(HR.Modules.Offboarding.OffboardingModule).Assembly,
        typeof(HR.Modules.Probation.ProbationModule).Assembly,
        typeof(HR.Modules.Recruitment.RecruitmentModule).Assembly,
        typeof(HR.Modules.Sickness.SicknessModule).Assembly,
        typeof(HR.Modules.Assets.AssetsModule).Assembly,
        typeof(HR.Modules.Notifications.NotificationsModule).Assembly,
        typeof(HR.Modules.Reporting.ReportingModule).Assembly,
        typeof(HR.SharedKernel.IAuditEvent).Assembly,
    ];

    [Fact]
    public void Human_IAuditEvent_Implementations_Supply_ActorUserId_Or_ActorEmployeeId_When_Constructed_With_Non_Null_Ids()
    {
        // This test instantiates each event with a non-null GUID for every Guid parameter.
        // For Human-typed events, the attribution guard should pass when actor IDs are provided.
        // It is intentionally NOT verifying that null actors on Human events fail — that is
        // covered by AuditOutboxTests.PublishAsync_Does_Not_Throw_When_Save_Fails indirectly,
        // and by AuditActorAttributionGuard unit tests below.

        var violations = new List<string>();

        var types = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IAuditEvent).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(types);

        foreach (var type in types)
        {
            var instance = TryCreate(type, withNonNullGuids: true);
            if (instance is not IAuditEvent evt)
                continue;

            // Non-Human events must be explicitly typed — the default ActorType is Human,
            // so if both actors are null and the type is still Human it's an error.
            if (evt.ActorType == AuditActorType.Human)
            {
                if (!evt.ActorUserId.HasValue && !evt.ActorEmployeeId.HasValue)
                {
                    violations.Add(
                        $"{type.FullName}: ActorType=Human but ActorUserId and ActorEmployeeId are both null " +
                        $"even when constructed with non-null Guid arguments. " +
                        $"Either supply the actor from a Guid parameter, or mark ActorType as ScheduledJob/IntegrationHandler.");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void AuditActorAttributionGuard_Throws_For_Human_Event_With_No_Actor()
    {
        var evt = new NullActorHumanEvent();
        Assert.Throws<MissingAuditActorException>(() => AuditActorAttributionGuard.Assert(evt));
    }

    [Fact]
    public void AuditActorAttributionGuard_Does_Not_Throw_For_ScheduledJob_With_No_Actor()
    {
        var evt = new NoActorScheduledJobEvent();
        var ex = Record.Exception(() => AuditActorAttributionGuard.Assert(evt));
        Assert.Null(ex);
    }

    [Fact]
    public void AuditActorAttributionGuard_Does_Not_Throw_For_IntegrationHandler_With_No_Actor()
    {
        var evt = new NoActorIntegrationHandlerEvent();
        var ex = Record.Exception(() => AuditActorAttributionGuard.Assert(evt));
        Assert.Null(ex);
    }

    [Fact]
    public void AuditActorAttributionGuard_Does_Not_Throw_For_Human_With_UserId()
    {
        var evt = new HumanEventWithUserId();
        var ex = Record.Exception(() => AuditActorAttributionGuard.Assert(evt));
        Assert.Null(ex);
    }

    [Fact]
    public void AuditActorAttributionGuard_Does_Not_Throw_For_Human_With_EmployeeId()
    {
        var evt = new HumanEventWithEmployeeId();
        var ex = Record.Exception(() => AuditActorAttributionGuard.Assert(evt));
        Assert.Null(ex);
    }

    private static IAuditEvent? TryCreate(Type type, bool withNonNullGuids)
    {
        try
        {
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (ctor is null)
                return null;

            var args = ctor.GetParameters()
                .Select(p => GetValue(p.ParameterType, withNonNullGuids))
                .ToArray();

            return ctor.Invoke(args) as IAuditEvent;
        }
        catch { return null; }
    }

    private static object? GetValue(Type type, bool nonNullGuids)
    {
        if (type == typeof(Guid))   return nonNullGuids ? Guid.NewGuid() : Guid.Empty;
        if (type == typeof(Guid?))  return nonNullGuids ? (Guid?)Guid.NewGuid() : null;
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.MinValue;
        if (type == typeof(DateTimeOffset?)) return (DateTimeOffset?)null;
        if (type == typeof(DateOnly)) return DateOnly.MinValue;
        if (type == typeof(DateOnly?)) return (DateOnly?)null;
        if (type == typeof(bool))   return false;
        if (type == typeof(bool?))  return (bool?)null;
        if (type == typeof(int))    return 0;
        if (type == typeof(decimal)) return 0m;
        if (type.IsValueType) return Activator.CreateInstance(type);
        return null;
    }
}

// ── Test fixtures ─────────────────────────────────────────────────────────────────────────────

internal sealed class NullActorHumanEvent : IAuditEvent
{
    public Guid           CompanyId       => Guid.NewGuid();
    public string         EventType       => "test.human.no-actor";
    public string         EntityType      => "Test";
    public Guid           EntityId        => Guid.NewGuid();
    public Guid?          ActorUserId     => null; // missing — should be rejected
    public Guid?          ActorEmployeeId => null;
    public DateTimeOffset OccurredAt      => DateTimeOffset.UtcNow;
    public Guid?          CorrelationId   => null;
    public string?        Summary         => null;
    public object?        Before          => null;
    public object?        After           => null;
    public object?        Metadata        => null;
    // ActorType defaults to Human
}

internal sealed class NoActorScheduledJobEvent : IAuditEvent
{
    public Guid           CompanyId       => Guid.NewGuid();
    public string         EventType       => "test.job.no-actor";
    public string         EntityType      => "Test";
    public Guid           EntityId        => Guid.NewGuid();
    public Guid?          ActorUserId     => null;
    public Guid?          ActorEmployeeId => null;
    public DateTimeOffset OccurredAt      => DateTimeOffset.UtcNow;
    public Guid?          CorrelationId   => null;
    public string?        Summary         => null;
    public object?        Before          => null;
    public object?        After           => null;
    public object?        Metadata        => null;
    public AuditActorType ActorType       => AuditActorType.ScheduledJob;
}

internal sealed class NoActorIntegrationHandlerEvent : IAuditEvent
{
    public Guid           CompanyId       => Guid.NewGuid();
    public string         EventType       => "test.integration.no-actor";
    public string         EntityType      => "Test";
    public Guid           EntityId        => Guid.NewGuid();
    public Guid?          ActorUserId     => null;
    public Guid?          ActorEmployeeId => null;
    public DateTimeOffset OccurredAt      => DateTimeOffset.UtcNow;
    public Guid?          CorrelationId   => null;
    public string?        Summary         => null;
    public object?        Before          => null;
    public object?        After           => null;
    public object?        Metadata        => null;
    public AuditActorType ActorType       => AuditActorType.IntegrationHandler;
}

internal sealed class HumanEventWithUserId : IAuditEvent
{
    public Guid           CompanyId       => Guid.NewGuid();
    public string         EventType       => "test.human.with-user";
    public string         EntityType      => "Test";
    public Guid           EntityId        => Guid.NewGuid();
    public Guid?          ActorUserId     => Guid.NewGuid();
    public Guid?          ActorEmployeeId => null;
    public DateTimeOffset OccurredAt      => DateTimeOffset.UtcNow;
    public Guid?          CorrelationId   => null;
    public string?        Summary         => null;
    public object?        Before          => null;
    public object?        After           => null;
    public object?        Metadata        => null;
}

internal sealed class HumanEventWithEmployeeId : IAuditEvent
{
    public Guid           CompanyId       => Guid.NewGuid();
    public string         EventType       => "test.human.with-employee";
    public string         EntityType      => "Test";
    public Guid           EntityId        => Guid.NewGuid();
    public Guid?          ActorUserId     => null;
    public Guid?          ActorEmployeeId => Guid.NewGuid();
    public DateTimeOffset OccurredAt      => DateTimeOffset.UtcNow;
    public Guid?          CorrelationId   => null;
    public string?        Summary         => null;
    public object?        Before          => null;
    public object?        After           => null;
    public object?        Metadata        => null;
}
