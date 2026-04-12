using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using UnitOfWorkAPI.Models.Database;

namespace UnitOfWorkAPI.Services;

public sealed class UnitOfWorkService : IUnitOfWorkService, IDisposable
{
    private readonly IDbContextFactory<UOWContext>? _dbContextFactory;
    private readonly ILogger<UnitOfWorkService> _logger;
    private readonly ILockRequestFactory _lockRequestFactory;

    // Shared context used for exclusive operations (transactional)
    private readonly UOWContext? _sharedContext;

    // Optional transaction starter (test seam / customization)
    private readonly Func<CancellationToken, Task<IDbContextTransaction>>? _transactionStarter;

    // Transaction for the current exclusive lock
    private IDbContextTransaction? _currentTransaction;
    private Guid? _currentLockId;
    private ILockRequest? _currentRequest;
    private bool _isLocked;

    // FIFO queue for lock requests
    private readonly object _queueLock = new();
    private readonly Queue<ILockRequest> _lockQueue = new();

    // Readers tracking and release coordination
    private readonly object _readersLock = new();
    private int _activeReaders;
    private bool _isReleasing;
    private TaskCompletionSource<bool>? _noActiveReadersTcs;

    // Batch size for SaveChanges to reduce risk of DB parameter limits
    private const int DefaultBatchSize = 500;

    // Original ctor (keeps existing behavior)
    public UnitOfWorkService(IDbContextFactory<UOWContext> dbContextFactory, ILogger<UnitOfWorkService> logger)
        : this(dbContextFactory, logger, transactionStarter: null, lockRequestFactory: null)
    {
    }

    // New ctor that accepts optional transaction starter (test seam) and optional lock request factory.
    public UnitOfWorkService(IDbContextFactory<UOWContext>? dbContextFactory, ILogger<UnitOfWorkService> logger, Func<CancellationToken, Task<IDbContextTransaction>>? transactionStarter, ILockRequestFactory? lockRequestFactory = null)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transactionStarter = transactionStarter;
        _lockRequestFactory = lockRequestFactory ?? new DefaultLockRequestFactory();

        if (_dbContextFactory != null)
        {
            try
            {
                _sharedContext = _dbContextFactory.CreateDbContext();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create shared UOWContext in UnitOfWorkService constructor.");
                throw;
            }
        }
    }

    // Test-only ctor convenience: create service without a DbContextFactory and only a transaction starter.
    public UnitOfWorkService(ILogger<UnitOfWorkService> logger, Func<CancellationToken, Task<IDbContextTransaction>> transactionStarter)
        : this(dbContextFactory: null, logger: logger, transactionStarter: transactionStarter, lockRequestFactory: null)
    {
        Console.WriteLine("This shouldn't be called by production");
    }

    public Task<Guid> GetDatabaseLockAsync(CancellationToken cancellationToken = default)
    {
        var request = _lockRequestFactory.Create(cancellationToken);

        lock (_queueLock)
        {
            _lockQueue.Enqueue(request);
            if (cancellationToken.IsCancellationRequested)
            {
                if (TryRemoveRequest(request))
                {
                    request.SetCanceled();
                }
                return request.Task;
            }
        }

        // Register a cancellation callback that attempts to remove while queued.
        if (cancellationToken.CanBeCanceled)
        {
            request.CancellationRegistration = cancellationToken.Register(() =>
            {
                lock (_queueLock)
                {
                    // If still queued, remove and mark canceled.
                    if (TryRemoveRequest(request))
                    {
                        request.SetCanceled();
                        return;
                    }

                    // If removal failed and the request is being granted or already granted, do nothing here.
                    // Grant path and held-lock registration handle cancellation.
                }
            }, useSynchronizationContext: false);
        }

        TryGrantNext();

        return request.Task;
    }

    private bool TryRemoveRequest(ILockRequest request)
    {
        if (request == null) return false;
        if (_lockQueue.Count == 0) return false;

        var found = false;
        var buffer = new List<ILockRequest>(_lockQueue.Count);
        while (_lockQueue.Count > 0)
        {
            var item = _lockQueue.Dequeue();
            if (!found && ReferenceEquals(item, request))
            {
                found = true;
                continue;
            }
            buffer.Add(item);
        }

        for (int i = 0; i < buffer.Count; i++)
        {
            _lockQueue.Enqueue(buffer[i]);
        }

        return found;
    }

    private void TryGrantNext()
    {
        ILockRequest? next = null;

        lock (_queueLock)
        {
            if (_isLocked) return;
            while (_lockQueue.Count > 0)
            {
                var peek = _lockQueue.Peek();
                if (peek.IsCanceled)
                {
                    _lockQueue.Dequeue();
                    continue;
                }
                next = _lockQueue.Dequeue();
                // Mark as being granted so cancellation callback knows it's no longer queued.
                next.IsGranting = true;
                break;
            }

            if (next == null) return;
            _isLocked = true;
        }

        _ = GrantRequestAsync(next);
    }

    private async Task GrantRequestAsync(ILockRequest request)
    {
        try
        {
            // If token already cancelled after dequeuing — honor cancellation immediately.
            if (request.Token.CanBeCanceled && request.Token.IsCancellationRequested)
            {
                try
                {
                    request.SetCanceled();
                }
                catch { /* best-effort */ }

                lock (_queueLock)
                {
                    _isLocked = false;
                }

                try
                {
                    request.CancellationRegistration?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Disposing cancellation registration after cancellation threw.");
                }

                TryGrantNext();
                return;
            }

            if(_sharedContext == null)
            {
                throw new InvalidOperationException("Shared DbContext is not configured (dbContextFactory was null).");
            }
            if(_sharedContext.Database == null)
            {
                throw new InvalidOperationException("shared DbContext.Database is not configured (_sharedContext.Database was null).");
            }

            _currentTransaction = _transactionStarter != null
                ? await _transactionStarter(request.Token).ConfigureAwait(false)
                : await _sharedContext.Database.BeginTransactionAsync(request.Token).ConfigureAwait(false);

            var lockId = Guid.NewGuid();

            lock (_queueLock)
            {
                _currentLockId = lockId;
                _currentRequest = request;
            }

            // Switch cancellation registration: queued removal reg is disposed; replace with lock-holder reg that will rollback on cancel.
            try
            {
                request.CancellationRegistration?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Disposing previous cancellation registration threw.");
            }

            if (request.Token.CanBeCanceled)
            {
                // Register a callback that triggers rollback/release for this lock if canceled by the holder.
                request.CancellationRegistration = request.Token.Register(() =>
                {
                    lock (_queueLock)
                    {
                        if (_currentLockId != lockId) return;
                        // Fire-and-forget rollback to avoid blocking the cancellation thread.
                        _ = HandleLockHolderCancellationAsync(lockId);
                    }
                }, useSynchronizationContext: false);
            }

            request.SetResult(lockId);
            _logger.LogInformation("Granted database lock {LockId}.", lockId);
        }
        catch (OperationCanceledException)
        {
            // Cancellation occurred while starting the transaction (or token already canceled).
            try
            {
                request.SetCanceled();
            }
            catch { /* best-effort */ }

            lock (_queueLock)
            {
                _isLocked = false;
            }

            try
            {
                request.CancellationRegistration?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Disposing cancellation registration after cancellation threw.");
            }

            TryGrantNext();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin transaction for queued lock request.");
            request.SetException(ex);

            lock (_queueLock)
            {
                _isLocked = false;
            }
            TryGrantNext();
        }
        finally
        {
            // Do not dispose the lock-holder registration here; it must remain while lock is held.
            // The queued removal registration was disposed above.
            request.IsGranting = false;
        }
    }

    private async Task HandleLockHolderCancellationAsync(Guid lockId)
    {
        // Called when the lock-holding request's token is canceled.
        // Attempt to rollback and release the lock, then continue granting next.
        try
        {
            IDbContextTransaction? tx = null;
            lock (_queueLock)
            {
                if (_currentLockId != lockId) return;
                tx = _currentTransaction;
            }

            if (tx != null)
            {
                try
                {
                    await tx.RollbackAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Rollback during holder cancellation threw.");
                }
            }
        }
        finally
        {
            lock (_queueLock)
            {
                try
                {
                    _currentTransaction?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Disposing transaction after holder cancellation threw.");
                }

                _currentTransaction = null;
                _currentLockId = null;

                try
                {
                    _currentRequest?.CancellationRegistration?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Disposing cancellation registration after holder cancellation threw.");
                }

                _currentRequest = null;
                _isLocked = false;
            }

            TryGrantNext();
        }
    }

    private async Task WaitIfReleasingAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? tcs = null;
        lock (_readersLock)
        {
            if (_isReleasing)
            {
                tcs = _noActiveReadersTcs;
            }
        }

        if (tcs != null)
        {
            try
            {
                await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Select was cancelled while waiting for ongoing release to complete.");
                throw;
            }
        }
    }

    private static IEnumerable<IList<T>> Batch<T>(IList<T> source, int batchSize)
    {
        for (var i = 0; i < source.Count; i += batchSize)
        {
            yield return source.Skip(i).Take(Math.Min(batchSize, source.Count - i)).ToList();
        }
    }

    public async Task<IEnumerable<T>> SelectAsync<T>(Func<UOWContext, IQueryable<T>> queryFactory, CancellationToken cancellationToken = default)
        where T : class
    {
        if (queryFactory == null) throw new ArgumentNullException(nameof(queryFactory));

        await WaitIfReleasingAsync(cancellationToken).ConfigureAwait(false);

        Interlocked.Increment(ref _activeReaders);

        try
        {
            if (_dbContextFactory == null) throw new InvalidOperationException("DbContextFactory is not configured for SelectAsync in this instance.");
            if (_sharedContext == null) throw new InvalidOperationException("SharedContext is not configured for SelectAsync in this instance.");
            // using var ctx = _dbContextFactory.CreateDbContext();
            IQueryable<T> query;
            try
            {
                query = queryFactory(_sharedContext) ?? Enumerable.Empty<T>().AsQueryable();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Select queryFactory threw an exception.");
                throw;
            }

            try
            {
                var list = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
                return list.AsEnumerable();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Select execution failed.");
                throw;
            }
        }
        finally
        {
            var newCount = Interlocked.Decrement(ref _activeReaders);
            if (newCount == 0)
            {
                lock (_readersLock)
                {
                    _noActiveReadersTcs?.TrySetResult(true);
                }
            }
        }
    }

    public async Task<int> InsertAsync<T>(IEnumerable<T> entities, Guid lockId, CancellationToken cancellationToken = default)
        where T : class
    {
        if (entities == null) throw new ArgumentNullException(nameof(entities));
        EnsureLockHeld(lockId);

        try
        {
            var list = entities as IList<T> ?? entities.ToList();
            if (!list.Any()) return 0;

            var total = 0;
            foreach (var batch in Batch(list, DefaultBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _sharedContext!.Set<T>().AddRange(batch);
                total += batch.Count;
                await _sharedContext!.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return total;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InsertAsync failed.");
            throw;
        }
    }

    public async Task<int> UpdateAsync<T>(IEnumerable<T> entities, Guid lockId, CancellationToken cancellationToken = default)
        where T : class
    {
        if (entities == null) throw new ArgumentNullException(nameof(entities));
        EnsureLockHeld(lockId);

        try
        {
            var list = entities as IList<T> ?? entities.ToList();
            if (!list.Any()) return 0;

            var total = 0;
            foreach (var batch in Batch(list, DefaultBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var item in batch)
                {
                    _sharedContext!.Set<T>().Update(item);
                }

                total += batch.Count;
                await _sharedContext!.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return total;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateAsync failed.");
            throw;
        }
    }

    public async Task<int> DeleteAsync<T>(IEnumerable<T> entities, Guid lockId, CancellationToken cancellationToken = default)
        where T : class
    {
        if (entities == null) throw new ArgumentNullException(nameof(entities));
        EnsureLockHeld(lockId);

        try
        {
            var list = entities as IList<T> ?? entities.ToList();
            if (!list.Any()) return 0;

            var total = 0;
            foreach (var batch in Batch(list, DefaultBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _sharedContext!.Set<T>().RemoveRange(batch);
                total += batch.Count;
                await _sharedContext!.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return total;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteAsync failed.");
            throw;
        }
    }

    public async Task ReleaseDataLockAsync(Guid lockId, DbTransactionOption option, CancellationToken cancellationToken = default)
    {
        if (_currentLockId == null || _currentLockId != lockId)
        {
            var ex = new InvalidOperationException("Invalid or no lock held by caller.");
            _logger.LogWarning(ex, "ReleaseDataLockAsync called with invalid lockId {LockId}.", lockId);
            throw ex;
        }

        lock (_readersLock)
        {
            if (_isReleasing)
            {
                _logger.LogWarning("ReleaseDataLockAsync called while already releasing.");
            }
            _isReleasing = true;
            _noActiveReadersTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_activeReaders == 0)
            {
                _noActiveReadersTcs.TrySetResult(true);
            }
        }

        try
        {
            await _noActiveReadersTcs!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_currentTransaction == null)
                {
                    _logger.LogWarning("ReleaseDataLockAsync: no active transaction found for lock {LockId}.", lockId);
                }
                else
                {
                    switch (option)
                    {
                        case DbTransactionOption.Commit:
                            await _currentTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        case DbTransactionOption.Rollback:
                            await _currentTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(option), option, null);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Option} transaction for lock {LockId}.", option, lockId);
                throw;
            }
        }
        finally
        {
            try
            {
                _currentTransaction?.Dispose();
                _currentTransaction = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Disposing transaction threw an exception.");
            }

            // Clear lock state and dispose lock-holder registration if present
            lock (_queueLock)
            {
                _currentLockId = null;
                _isLocked = false;

                try
                {
                    _currentRequest?.CancellationRegistration?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Disposing cancellation registration threw.");
                }
                _currentRequest = null;
            }

            lock (_readersLock)
            {
                _isReleasing = false;
                _noActiveReadersTcs = null;
            }

            TryGrantNext();
        }
    }

    private void EnsureLockHeld(Guid lockId)
    {
        if (_currentLockId == null || _currentLockId != lockId)
        {
            var ex = new InvalidOperationException("Exclusive lock not held by caller or invalid lockId.");
            _logger.LogWarning(ex, "Operation attempted with invalid or missing lockId {LockId}.", lockId);
            throw ex;
        }
    }

    public void Dispose()
    {
        try
        {
            _currentTransaction?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing current transaction in Dispose.");
        }

        try
        {
            _sharedContext?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing shared context in Dispose.");
        }

        try
        {
            lock (_queueLock)
            {
                _currentRequest?.CancellationRegistration?.Dispose();
                while (_lockQueue.Count > 0)
                {
                    var r = _lockQueue.Dequeue();
                    try { r.CancellationRegistration?.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing cancellation registrations in Dispose.");
        }
    }
}
