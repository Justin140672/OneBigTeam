using System.Data.Common;
using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Persistence;

internal sealed class CandidateDocumentScanDispatchInterceptor(
    IBackgroundJobClient backgroundJobClient,
    ILogger<CandidateDocumentScanDispatchInterceptor> logger)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly List<Guid> _capturedForCurrentSave = [];
    private readonly List<Guid> _awaitingCommit = [];


    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        OnSaved(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        OnSaved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _capturedForCurrentSave.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _capturedForCurrentSave.Clear();
        return Task.CompletedTask;
    }


    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => DispatchAwaitingCommit();

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        DispatchAwaitingCommit();
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _awaitingCommit.Clear();

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => _awaitingCommit.Clear();

    public Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return Task.CompletedTask;
    }


    private void Capture(DbContext? context)
    {
        _capturedForCurrentSave.Clear();
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<CandidateDocument>())
        {
            if (entry.State == EntityState.Added)
                _capturedForCurrentSave.Add(entry.Entity.Id);
        }
    }

    private void OnSaved(DbContext? context)
    {
        if (_capturedForCurrentSave.Count == 0)
            return;

        var saved = _capturedForCurrentSave.ToList();
        _capturedForCurrentSave.Clear();

        if (context?.Database.CurrentTransaction is not null)
        {
            _awaitingCommit.AddRange(saved);
            return;
        }

        Dispatch(saved);
    }

    private void DispatchAwaitingCommit()
    {
        if (_awaitingCommit.Count == 0)
            return;

        var committed = _awaitingCommit.ToList();
        _awaitingCommit.Clear();
        Dispatch(committed);
    }

    private void Dispatch(IEnumerable<Guid> documentIds)
    {
        foreach (var documentId in documentIds)
        {
            try
            {
                backgroundJobClient.Enqueue<ScanCandidateDocumentJob>(job => job.ScanAsync(documentId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Could not dispatch the malware scan for candidate document {DocumentId}; ReconcileCandidateDocumentScansJob will pick it up.",
                    documentId);
            }
        }
    }
}
