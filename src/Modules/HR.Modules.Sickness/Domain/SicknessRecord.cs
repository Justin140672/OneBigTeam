namespace HR.Modules.Sickness.Domain;

internal sealed class SicknessRecord
{
    private SicknessRecord() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid CategoryId { get; private set; }
    public SicknessStatus Status { get; private set; }
    public DateOnly StartDate { get; private set; }
    public SicknessDayPart StartDayPart { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public SicknessDayPart? EndDayPart { get; private set; }
    public DateOnly? ReturnToWorkDate { get; private set; }
    public SicknessEvidenceStatus EvidenceStatus { get; private set; }
    public string? EvidenceNotes { get; private set; }
    public string? Notes { get; private set; }
    public decimal? TotalDays { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SicknessRecord Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid categoryId,
        DateOnly startDate,
        SicknessDayPart startDayPart,
        DateOnly? endDate,
        SicknessDayPart? endDayPart,
        decimal? totalDays,
        string? notes,
        SicknessEvidenceStatus evidenceStatus,
        DateTimeOffset now)
    {
        return new SicknessRecord
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            Status = endDate.HasValue ? SicknessStatus.Closed : SicknessStatus.Active,
            StartDate = startDate,
            StartDayPart = startDayPart,
            EndDate = endDate,
            EndDayPart = endDayPart,
            TotalDays = totalDays,
            EvidenceStatus = evidenceStatus,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Close(
        DateOnly endDate,
        SicknessDayPart endDayPart,
        DateOnly? returnToWorkDate,
        decimal? totalDays,
        SicknessEvidenceStatus evidenceStatus,
        string? evidenceNotes,
        DateTimeOffset now)
    {
        Status = SicknessStatus.Closed;
        EndDate = endDate;
        EndDayPart = endDayPart;
        ReturnToWorkDate = returnToWorkDate;
        TotalDays = totalDays;
        EvidenceStatus = evidenceStatus;
        EvidenceNotes = evidenceNotes;
        UpdatedAt = now;
    }

    public void ReceiveEvidence(DateTimeOffset now)
    {
        EvidenceStatus = SicknessEvidenceStatus.Received;
        UpdatedAt = now;
    }

    /// <summary>
    /// Used by FitNoteEvidenceRequestService when it creates an evidence request for a record whose
    /// EvidenceStatus wasn't already Pending (e.g. a legacy/imported record evaluated by the daily
    /// job). Never called for Received/Waived records — the service checks that first.
    /// </summary>
    public void MarkEvidencePending(DateTimeOffset now)
    {
        EvidenceStatus = SicknessEvidenceStatus.Pending;
        UpdatedAt = now;
    }

    public void Update(
        Guid categoryId,
        DateOnly startDate,
        SicknessDayPart startDayPart,
        DateOnly? endDate,
        SicknessDayPart? endDayPart,
        DateOnly? returnToWorkDate,
        decimal? totalDays,
        SicknessEvidenceStatus evidenceStatus,
        string? evidenceNotes,
        string? notes,
        DateTimeOffset now)
    {
        CategoryId = categoryId;
        StartDate = startDate;
        StartDayPart = startDayPart;
        EndDate = endDate;
        EndDayPart = endDayPart;
        ReturnToWorkDate = returnToWorkDate;
        TotalDays = totalDays;
        EvidenceStatus = evidenceStatus;
        EvidenceNotes = evidenceNotes;
        Notes = notes;
        UpdatedAt = now;
    }
}
