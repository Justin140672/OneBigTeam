# Ticket 4 — Keep large exports within defined resource limits

Scope: the **OrganisationDataExport** pipeline (account-closure export) — the `OrganisationDataExportBuildJob`
orchestrator, the `OrganisationDataExportPackageBuilder`, `IOrganisationDataExportStorage`, and the
cross-module `I*DataExportSource` / `IDocumentDataExportManifest` read contracts. Builds on Ticket 3
(ownership leases, recovery sweep, artefact cleanup) which is already merged.

---

## 1. Representative large dataset

A "large but realistic" single-tenant organisation used for budgeting and verification:

| Dimension | Representative value |
|---|---|
| Employees | 2,000 (plus ~150 departments, ~2,000 emergency contacts) |
| Leave / sickness / recruitment / probation rows | ~120,000 rows combined |
| Committed audit-log rows | ~1,500,000 rows |
| Documents (files to embed) | 15,000 objects |
| Total document byte size | ~8 GiB |
| Largest single document | ~250 MiB (e.g. an induction video / scanned handbook) |
| Typical document | 0.5–4 MiB PDF / image |
| Resulting ZIP archive | ~7.5 GiB (documents are already-compressed formats; CSV portion compresses well) |

CSV portion of the archive (all module tables serialised): ~350 MiB uncompressed, dominated by the
audit log. Everything else is well under 20 MiB.

---

## 2. Resource budgets

| Resource | Budget | Enforced by |
|---|---|---|
| Max simultaneous export builds (per worker process) | **2** | `OrganisationDataExportConcurrencyGate` (`SemaphoreSlim`) |
| Queue wait before a build gives up its slot attempt | **10 min**, then throws `OrganisationDataExportSlotUnavailableException` → Hangfire re-queues with back-off | `OrganisationDataExportConcurrencyGate.AcquireAsync` |
| Temp-disk per export (the archive being assembled) | **12 GiB** hard ceiling; the build fails if the partial archive exceeds it | `OrganisationDataExportWorkspace.EnsureWithinBudget` |
| Temp-disk total across all concurrent exports | **26 GiB**; a new workspace is refused if the work root already holds this much | `OrganisationDataExportWorkspaceFactory.CreateWorkspace` |
| Free-disk safety margin | new workspace refused if the drive has < 14 GiB free | `OrganisationDataExportWorkspaceFactory.CreateWorkspace` |
| Open document streams held at once | **1** (open → copy → dispose, in a single pass) | `OrganisationDataExportPackageBuilder.BuildToStreamAsync` |
| Copies of the ZIP in memory | **0** — the archive only ever exists as one temp file on disk | builder writes straight to the workspace `FileStream`; upload streams from it |
| Peak managed memory attributable to the pipeline | one `CopyToAsync` buffer (80 KiB) + one module's table rows; the audit log now streams in 1,000-row keyset pages (~<1 MiB/page) instead of ~350 MiB | — |

All budgets are constants on `OrganisationDataExportResourceLimits` (in `HR.Infrastructure.Abstractions`)
so they can be tuned centrally.

### Failure behaviour when a limit is exceeded

- **Concurrency**: the build never claims an attempt; it throws `OrganisationDataExportSlotUnavailableException`
  before `BeginAttemptAsync`, so no `AttemptCount` is consumed. Hangfire retries the job later; the
  `RecoverStalledOrganisationDataExports` sweep is the long-stop.
- **Temp-disk exhaustion** (either ceiling, or free-space margin, or per-export ceiling hit mid-build):
  the build stops, the workspace is deleted, the renewal loop is drained, the export is marked
  **Failed** with reason *"The export could not be completed because temporary working storage was
  exhausted."*, and an administrative alert is raised (`ReportGeneration` / `Warning`, dedup key
  `organisation-data-export-temp-capacity:{companyId}`, `Reason = null` so it does **not** send an
  operations email — consistent with other non-missing-document report failures). The export can be
  re-requested by the administrator once capacity frees up.

---

## 3. Processing approach

1. **Claim a concurrency slot** (`IOrganisationDataExportConcurrencyGate`). Released on every exit path.
2. **Claim the attempt + ownership lease** (unchanged Ticket 3 `BeginAttemptAsync`); start the
   background lease-renewal loop (unchanged — it keeps heart-beating through every step below).
3. **Create a bounded temp workspace** (`IOrganisationDataExportWorkspaceFactory.CreateWorkspace`) —
   checks total-root and free-disk budgets, then creates `…/organisation-export-work/{exportId}-{rand}/`.
4. **Assemble the ZIP straight to the workspace file** (`BuildToStreamAsync`):
   - Module tables are streamed in as an `IAsyncEnumerable<DataExportTable>` produced **one source at a
     time** (`StreamTablesAsync`) — only one module's rows are in memory at any moment, then released.
   - Each table is written as an RFC 4180 CSV directly into its zip entry (already streamed via
     `StreamWriter`).
   - Document files are processed **strictly one at a time**: open the read stream, copy it into the
     zip entry, dispose it, move on. Never more than one document stream open.
   - After every entry, `EnsureWithinBudget(currentFileLength)` enforces the per-export disk ceiling.
   - The cancellation token (linked to host shutdown **and** ownership loss) is checked before every
     table and every document.
   - Ticket-3 filename-uniquification is preserved verbatim.
5. **Missing documents**: if any expected document stream comes back `null`, the builder finishes
   enumerating (to count them all, still one stream at a time) but adds nothing further to the archive
   and returns the missing list. The job then deletes the workspace, drains renewal, marks the export
   **Failed for missing documents** (Ticket 3 ownership-guarded), and raises the existing
   missing-documents alert (`Reason = MissingDocumentExport`, which does queue an operations email).
   Documents-module rows are never touched. This preserves the pre-existing behaviour exactly.
6. **Upload**: stream the workspace file to `IOrganisationDataExportStorage.UploadAsync` from
   `Position = 0`, wrapped in a non-disposing adapter so the bounded retry loop can re-seek and
   re-send the same file without buffering. `FileSizeBytes` is the file length (`FileStream.Length`),
   never a materialised `byte[]`.
7. **Publish** via the ownership-guarded `MarkCompletedAsync` (unchanged), sweep sibling attempt
   archives (unchanged), publish the completed integration event (unchanged).
8. **Cleanup**: a `finally` disposes the workspace (recursive delete) on success, failure and
   cancellation. Orphans left by a hard process kill are removed by
   `OrganisationDataExportWorkspaceFactory.SweepOrphans`, invoked at the top of the existing
   `RecoverStalledOrganisationDataExports` sweep (every 5 min) for any workspace dir older than 6 h.

### Integration with Ticket 3 ownership & cleanup

- The lease-renewal loop is started before step 3 and drained (`StopRenewalAsync`) before **every**
  terminal write (complete / fail / missing-docs fail), exactly as Ticket 3 requires. Streaming reads,
  compression and upload all happen while the loop is live, so a multi-minute build keeps its lease.
- Ownership loss (renewer returns `false`) cancels the linked token; the build unwinds with no
  upload / complete / fail, the workspace is deleted in `finally`, and Ticket 3 recovery takes over.
- Per-attempt object keys (`…/{exportId}/{attemptToken}.zip`) and the winner-sweeps-siblings /
  recovery-sweeps-attempts / retryable-artefact-cleanup logic are all unchanged.
- Temp **local** workspace files are a new artefact class; they are cleaned on the same three paths
  (success/failure/cancel) plus the new orphan sweep, mirroring the Ticket 3 attempt-archive rules.

### Database-read batching decision

The `I*DataExportSource.GetTablesAsync` contracts return a materialised `IReadOnlyList<DataExportTable>`
per module. Changing all six cross-module contracts to `IAsyncEnumerable` row streaming is a large
blast radius across six modules for limited benefit: for the representative dataset every module
except audit produces < 20 MiB of rows, and the job now processes **one source at a time** and streams
each table's CSV straight to disk, so only one module's rows are resident at once and then GC'd.
The **audit log** is the one genuinely unbounded reader (~1.5M rows). **Done (follow-up, 2026-09-09):**
`AuditDataExportSource` now returns a streamed `DataExportTable` (`DataExportTable.Streamed`) whose
rows come from keyset pagination over `(OccurredAt, Id)` — 1,000 rows/page, `OrderByDescending`
`OccurredAt` then `Id`, `Take(PageSize)` per page (no `Skip`/`Take`). The package builder writes each
row straight to the CSV entry via `WriteCsvAsync` and never buffers the set into a list. The
cross-module `I*DataExportSource.GetTablesAsync` contract shape is unchanged — only the audit
implementation and the `DataExportTable` record (new optional `RowStream`) changed; the other five
sources keep the eager shape as their row counts are bounded by organisation size.

---

## 4. Acceptance criterion → implementation → verification

| Criterion | Implementation | Verification |
|---|---|---|
| Read DB records in bounded batches where necessary | Done: sources processed one-at-a-time with CSV streamed to disk; the audit log (the one unbounded reader) uses keyset pagination (`AuditDataExportSource.StreamRowsAsync`, 1,000-row pages on `(OccurredAt, Id)`) streamed row-by-row into the CSV by `OrganisationDataExportPackageBuilder.WriteCsvAsync` — never a whole-set `ToListAsync` or a second formatted-row list | `AuditDataExportSourceTests` (multi-page, shared-`OccurredAt` page-boundary, no skip/dup, ordering, company isolation, cancellation mid-enumeration); `OrganisationDataExportPackageBuilderTests` streamed-CSV byte-match / escaping / non-materialisation / cancellation; `OrganisationDataExportBuildJobTests` full job with a 5,000-row streamed audit source asserting max-live rows ≤ 2 |
| Retrieve/process/dispose document streams incrementally, one at a time | `BuildToStreamAsync` opens → copies → disposes each document in a single loop; asserts at most one open at a time | `OrganisationDataExportPackageBuilderTests.BuildToStream_Opens_Documents_One_At_A_Time`; `…_Disposes_Each_Document_Stream` |
| Never hold the complete ZIP (or copies) in memory | builder writes to the workspace `FileStream`; upload streams from it; `FileSizeBytes` from `FileStream.Length` | `OrganisationDataExportBuildJobTests.Large_Archive_Is_Never_Fully_Buffered` (tracking stream asserts max in-memory bytes); builder test writes to a `ThrowOnGetBufferStream` |
| Bound temp-disk usage + clear failure behaviour | `OrganisationDataExportWorkspaceFactory.CreateWorkspace` (free-space margin + **atomic combined-budget reservation**: measured on-disk/orphan bytes + sum of outstanding per-export reservations + this reservation must stay under `MaxTotalWorkspaceBytes`, released on workspace disposal) plus **`OrganisationDataExportArchiveBudgetStream`** — a write-through counting stream wrapping the archive `FileStream` that throws `OrganisationDataExportTempCapacityException` the instant a write (including ZIP central-directory / finalisation) would cross the per-export ceiling, before any bytes are written — plus the belt-and-braces post-entry `EnsureWithinBudget`; on breach → Failed + admin alert (`organisation-data-export-temp-capacity:{companyId}`, `Reason` null) + workspace deleted + no upload | `OrganisationDataExportArchiveBudgetStreamTests.*` (nothing written on breach, boundary, mid-sequence, async+sync, no inner dispose); `OrganisationDataExportBudgetedBuildTests.*` (oversized streamed table / document / ZIP-finalisation all trip mid-write through the real builder + workspace, archive file never exceeds ceiling); `OrganisationDataExportWorkspaceFactoryTests.*` (reservation refuses the Nth+1 concurrent workspace, dispose releases it, orphan bytes count against it); `OrganisationDataExportBuildJobResourceLimitTests.*` (mid-write breach → Failed + one dedup alert + no upload + no event + workspace deleted) |
| Maintain ownership renewal during slow reads/compression/upload | renewal loop started before workspace creation, drained only before terminal writes; unchanged Ticket 3 loop | `OrganisationDataExportBuildJobTests.Slow_Streaming_Build_And_Upload_Keep_Renewing_The_Lease` |
| Stop safely after cancellation or ownership loss | linked `processingCts`; token checked per table/per document, **inside both the streamed AND the eager `Rows` CSV loops** and before the header, with cancellation-aware `WriteAsync(ReadOnlyMemory<char>, ct)`; token checked in upload; `finally` deletes workspace | `…Cancellation_Mid_Stream_Deletes_The_Workspace_And_Does_Not_Upload`; `…Ownership_Lost_Mid_Stream_Aborts_Cleanly`; `OrganisationDataExportPackageBuilderTests` eager-`Rows`-loop cancellation + streamed cancellation |
| Clean up temp resources after success/failure/cancellation; recover process-interruption leftovers | `using var workspace` + `finally`; `SweepOrphans` from the recovery sweep | `…Workspace_Is_Deleted_On_{Success,Failure,Cancellation}`; `OrganisationDataExportWorkspaceFactoryTests.SweepOrphans_Removes_Stale_Dirs_Only`; `RecoverStalledOrganisationDataExportsJobTests.Sweeps_Orphaned_Workspaces` |
| Preserve missing-file failure, unique filenames, ownership-guarded publication | missing list from `BuildToStreamAsync`; `Uniquify` unchanged; `MarkFailedDueToMissingDocumentsAsync` / `MarkCompletedAsync` unchanged | existing `OrganisationDataExportBuildJobTests` missing-docs + uniquify tests retained and green; `OrganisationDataExportPackageBuilderTests` uniquify tests retained |
| Demonstrate compliance using the complete path + real (or faithful streaming) storage | `OrganisationDataExportBuildJobTests` run the real `OrganisationDataExportPackageBuilder` + real `LocalOrganisationDataExportStorage` against a temp dir; integration test `OrganisationDataExportResourceLimitsTests` exercises the enqueue→build→download path | see §5 for what is / isn't covered |
| Multiple concurrent exports at the configured limit | `OrganisationDataExportConcurrencyGate` | `OrganisationDataExportConcurrencyGateTests.*`; `OrganisationDataExportBuildJobTests.Third_Concurrent_Build_Waits_For_A_Slot` |
| Slow document retrieval and slow upload | `FakeDocumentManifest.OpenDelay`, `LocalOrganisationDataExportStorage` wrapped with a slow-copy decorator in tests | `…Slow_Streaming_Build_And_Upload_Keep_Renewing_The_Lease` |
| Failed cleanup followed by successful retry | workspace delete failure is swallowed and logged; orphan sweep retries | `OrganisationDataExportWorkspaceFactoryTests.SweepOrphans_Is_Idempotent_And_Retries_After_A_Locked_File_Is_Released` |

---

## 5. Explicitly unverified / residual risks

- ~~**Audit-log reader is not batched.**~~ **Done 2026-09-09.** `AuditDataExportSource` streams audit
  rows via `(OccurredAt, Id)` keyset pagination (1,000-row pages) and the package builder writes them
  row-by-row into the CSV entry, so the ~1.5M-row worst case never sits in memory. Peak managed
  memory attributable to the audit source is now one page (~<1 MiB) rather than ~350 MiB.

- **Supabase implementations** (`SupabaseOrganisationDataExportStorage`, `SupabaseDocumentStorageService`)
  are exercised only by unit-level tests of their request shaping, not against a live Supabase. The
  build-job tests use the real `LocalOrganisationDataExportStorage` (true disk streaming) as the
  faithful substitute. `SupabaseDocumentStorageService.OpenReadStreamAsync` already uses
  `HttpCompletionOption.ResponseHeadersRead` (genuine streaming); `SupabaseOrganisationDataExportStorage.UploadAsync`
  now receives a seekable `FileStream` so `Content-Length` is known without buffering. **Not
  load-tested at 8 GiB against real Supabase.**
- **`SupabaseOrganisationDataExportStorage.OpenAsync`** (download path, used by
  `DownloadOrganisationDataExport`) still copies the archive into a `MemoryStream`. That is the
  *download* side, out of this ticket's build-pipeline scope, but it is a real large-export memory
  risk on the API host. **Flagged as a follow-up.**
- **Non-audit module row counts** are assumed bounded by organisation size. If a tenant has an
  extreme leave/recruitment history the single-source-at-a-time step still materialises that one
  module's rows. Not batched. Low likelihood; follow-up if it ever bites.
- **Process-wide (not cluster-wide) concurrency cap.** The gate is a per-process `SemaphoreSlim`.
  The deployment runs a single background-job worker process, so this is currently sufficient; a
  multi-worker rollout would need a DB-backed slot table. **Documented, not implemented.**
- **Disk budget is advisory against a shared temp volume.** `DriveInfo.AvailableFreeSpace` and a
  recursive directory-size walk are point-in-time; a burst of other temp usage between the check and
  the write can still exhaust the disk, in which case the `FileStream` write throws `IOException`
  and the generic failure path (Failed + retry) catches it. Verified via the injected
  capacity-exception path, not via a real full disk.

- **Combined-budget reservation is per-process, not per-volume or cluster-wide** (2026-09-09). The
  reservation total lives on the singleton `OrganisationDataExportWorkspaceFactory` instance, so it
  only coordinates builds inside one worker process (matching the single-worker deployment and the
  existing per-process concurrency gate). It reserves the full per-export ceiling
  (`Min(MaxArchiveBytesPerExport, MaxTotalWorkspaceBytes)`) up front — and that same figure is used as
  the workspace's effective archive ceiling so a reservation always covers the most a workspace may
  write. A second temp consumer on the same volume outside this factory is still only caught by the
  free-space margin / `IOException` backstop.
- **Capacity accounting fixed (2026-09-10, Ticket 4 final follow-up).** `CreateWorkspace` previously
  added *all* bytes on disk (including bytes inside active workspaces) *plus* the full set of active
  reservations *plus* the new reservation, double-counting active archive bytes and wrongly refusing a
  second export once the first had written a few GiB. Admission is now
  `orphan/unreserved bytes + Σ active reservations + requested reservation ≤ MaxTotalWorkspaceBytes`,
  with bytes inside active workspace directories excluded from the orphan figure. Reservations are
  tracked per active workspace directory and released exactly once on disposal or creation failure;
  files left behind by a failed delete revert to counting as orphan bytes on the next admission.
- **Mid-write per-export enforcement covers writes through the archive stream only** (2026-09-09).
  `OrganisationDataExportArchiveBudgetStream` counts every byte the ZIP writer and document copies push
  through it, including finalisation, and aborts the crossing write before it lands. It does not bound
  transient compressor internal buffers (bounded by one entry) or the OS file-cache.
- **PostgreSQL audit pagination** now has a real-Postgres integration test
  (`OrganisationDataExportAuditPaginationTests`) seeding > 2 keyset pages with a block of rows sharing
  one `OccurredAt` across the page boundary plus a second tenant, asserting exactly-once, isolation
  and `(OccurredAt, Id)` desc ordering — in addition to the in-memory `AuditDataExportSourceTests`.
  Not run at the ~1.5M-row worst case.
