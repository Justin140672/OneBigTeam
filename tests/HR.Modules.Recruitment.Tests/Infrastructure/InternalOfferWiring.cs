using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeEmployeeNameReader : IEmployeeNameReader
{
    private readonly Dictionary<Guid, string> _names = [];

    public FakeEmployeeNameReader Add(Guid employeeId, string name)
    {
        _names[employeeId] = name;
        return this;
    }

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            employeeIds.Where(_names.ContainsKey).Distinct().ToDictionary(id => id, id => _names[id]));
}

internal sealed class FakePermissionAuthorizationService : HR.SharedKernel.IAuthorizationService
{
    private readonly HashSet<(Guid UserId, Guid PermissionId)> _grants = [];

    public FakePermissionAuthorizationService Grant(Guid userId, Guid permissionId)
    {
        _grants.Add((userId, permissionId));
        return this;
    }

    public Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default) =>
        Task.FromResult(_grants.Contains((userId, permissionId)));

    public Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(_grants.Where(g => g.UserId == userId).Select(g => g.PermissionId).ToHashSet());

    public Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}

internal sealed class InternalOfferWiring
{
    private InternalOfferWiring()
    {
    }

    public FakeTaskCreator Creator { get; } = new();
    public FakeTaskCanceller Canceller { get; } = new();
    public FakeTaskCompleter Completer { get; } = new();
    public FakeTaskResolution Resolution { get; private set; } = null!;
    public FakeNotificationWriter Notifications { get; } = new();
    public FakeEmployeeApplicantReader Applicants { get; } = new();
    public FakeEmployeeNameReader Names { get; } = new();
    public FakePositionProfileReader Positions { get; private set; } = null!;
    public OfferTermsSnapshotFactory Snapshot { get; private set; } = null!;
    public InternalOfferTaskEffectsService Effects { get; private set; } = null!;

    public static InternalOfferWiring For(
        RecruitmentDbContext db,
        IClock clock,
        FakePositionProfileReader? positions = null)
    {
        var wiring = new InternalOfferWiring();
        wiring.Resolution = new FakeTaskResolution(wiring.Completer, wiring.Canceller);
        wiring.Positions = positions ?? new FakePositionProfileReader();
        wiring.Snapshot = new OfferTermsSnapshotFactory(wiring.Positions, FakeEmploymentTypeReader.Permissive(), wiring.Names);
        wiring.Effects = new InternalOfferTaskEffectsService(
            db, wiring.Creator, wiring.Canceller, wiring.Resolution, wiring.Notifications, clock,
            NullLogger<InternalOfferTaskEffectsService>.Instance);
        return wiring;
    }
}

internal static class OfferHandlerFactory
{
    public static HR.Modules.Recruitment.Features.OfferCandidate.OfferCandidateHandler Create(
        RecruitmentDbContext db,
        IClock clock,
        FakePositionProfileReader positions,
        RecruitmentStageChangeRecorder recorder,
        FakeCompanyRecruitmentSettingsReader settings,
        FakeAuditPublisher audit,
        InternalOfferWiring? wiring = null)
    {
        wiring ??= InternalOfferWiring.For(db, clock, positions);
        return new(db, clock, positions, recorder, settings, audit, wiring.Applicants, wiring.Snapshot, wiring.Effects);
    }
}
