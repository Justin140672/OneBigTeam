namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// A per-company configurable pipeline stage, replacing the previously fixed
/// <c>ApplicationStatus</c> enum (ticket #97). Recruitment administrators can add, edit, reorder,
/// activate and deactivate stages freely, including inserting new stages anywhere in the pipeline.
/// A stage is never hard-deleted once referenced by an <see cref="Application"/> — deactivation via
/// <see cref="SetActiveStatus"/> is the only supported removal path, so historical
/// Application.CurrentStageId references always remain resolvable.
/// </summary>
internal sealed class RecruitmentStage
{
    private RecruitmentStage() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsTerminal { get; private set; }
    public RecruitmentStageTerminalOutcome TerminalOutcome { get; private set; }

    /// <summary>
    /// DSH-04: optional explicit metric role for this stage (see <see cref="RecruitmentStagePurpose"/>).
    /// Always <c>null</c> for terminal stages.
    /// </summary>
    public RecruitmentStagePurpose? Purpose { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static RecruitmentStage Create(
        Guid id,
        Guid companyId,
        string name,
        int displayOrder,
        bool isTerminal,
        RecruitmentStageTerminalOutcome terminalOutcome,
        DateTimeOffset now,
        RecruitmentStagePurpose? purpose = null) => new()
    {
        Id              = id,
        CompanyId       = companyId,
        Name            = name.Trim(),
        DisplayOrder    = displayOrder,
        IsActive        = true,
        IsTerminal      = isTerminal,
        TerminalOutcome = isTerminal ? terminalOutcome : RecruitmentStageTerminalOutcome.None,
        Purpose         = isTerminal ? null : purpose,
        CreatedAt       = now,
        UpdatedAt       = now,
    };

    public void UpdateDetails(
        string name,
        bool isTerminal,
        RecruitmentStageTerminalOutcome terminalOutcome,
        DateTimeOffset now,
        RecruitmentStagePurpose? purpose = null)
    {
        Name            = name.Trim();
        IsTerminal      = isTerminal;
        TerminalOutcome = isTerminal ? terminalOutcome : RecruitmentStageTerminalOutcome.None;
        Purpose         = isTerminal ? null : purpose;
        UpdatedAt       = now;
    }

    public void SetDisplayOrder(int displayOrder, DateTimeOffset now)
    {
        DisplayOrder = displayOrder;
        UpdatedAt    = now;
    }

    // Deactivating never deletes the row: historical Application.CurrentStageId references must
    // remain resolvable, mirroring ExternalRecruiter.SetActiveStatus's rationale.
    public void SetActiveStatus(bool isActive, DateTimeOffset now)
    {
        IsActive  = isActive;
        UpdatedAt = now;
    }
}
