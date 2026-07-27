namespace HR.Infrastructure.Abstractions;

// Implemented by HR.Modules.Documents (the owning module for shared company document
// acknowledgements). Replays every historical acknowledgement as a
// SharedCompanyDocumentAcknowledgedIntegrationEvent so that HR.Modules.Employees' timeline
// backfill can populate entries for acknowledgements that pre-date the timeline feature — the
// same event shape and trigger condition as the live AcknowledgeSharedCompanyDocument handler,
// just driven from existing data instead of a fresh acknowledgement.
public interface ISharedCompanyDocumentAcknowledgementHistoryReplayer
{
    // Returns the number of historical acknowledgement events replayed, so the backfill
    // orchestrator can report an accurate processed count for this source.
    Task<int> ReplaySharedCompanyDocumentAcknowledgedAsync(Guid companyId, CancellationToken cancellationToken);
}
