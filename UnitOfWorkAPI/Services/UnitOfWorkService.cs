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

/// <summary>
/// Singleton-style service that mediates concurrent readonly access and serialized exclusive write transactions.
/// </summary>
public sealed class UnitOfWorkService : IUnitOfWorkService
{
    private readonly IDbContextFactory<UOWContext> _dbContextFactory;
    private readonly ILogger<UnitOfWorkService> _logger;

    // Shared context used for exclusive operations (transactional)
    private readonly UOWContext _sharedContext;

    // Transaction for the current exclusive lock
    private IDbContextTransaction? _currentTransaction;
    private Guid? _currentLockId;
    private bool _isLocked;

    // FIFO queue for lock requests
    private readonly object _queueLock = new();
    private readonly Queue<LockRequest> _lockQueue = new();

    // Readers tracking and release coordination
    private readonly object _readersLock = new();
    private int _activeReaders;
    private bool _isReleasing;
    private TaskCompletionSource<bool>? _noActiveReadersTcs;

    // Batch size for SaveChanges to reduce risk of DB parameter limits
    private const int DefaultBatchSize = 500;

    public UnitOfWorkService(IDbContextFactory<UOWContext> dbContextFactory, ILogger<UnitOfWorkService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

    public Task<Guid> GetDatabaseLockAsync(CancellationToken cancellationToken = default)
    {
        var request = new LockRequest(cancellationToken);

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

        if (cancellationToken.CanBeCanceled)
        {
            request.CancellationRegistration = cancellationToken.Register(() =>
            {
                lock (_queueLock)
                {
                    if (TryRemoveRequest(request))
                    {
                        request.SetCanceled();
                    }
                }
            });
        }

        TryGrantNext();

        return request.Task;
    }

    public async Task<IEnumerable<T>> SelectAsync<T>(Func<UOWContext, IQueryable<T>> queryFactory, CancellationToken cancellationToken = default)
        where T : class
    {
        if (queryFactory == null) throw new ArgumentNullException(nameof(queryFactory));

        await WaitIfReleasingAsync(cancellationToken).ConfigureAwait(false);

        Interlocked.Increment(ref _activeReaders);

        try
        {
            using var ctx = _dbContextFactory.CreateDbContext();
            IQueryable<T> query;
            try
            {
                query = queryFactory(ctx) ?? Enumerable.Empty<T>().AsQueryable();
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
                _sharedContext.Set<T>().AddRange(batch);
                total += batch.Count;
                await _sharedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
                    _sharedContext.Set<T>().Update(item);
                }

                total += batch.Count;
                await _sharedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
                _sharedContext.Set<T>().RemoveRange(batch);
                total += batch.Count;
                await _sharedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

            lock (_queueLock)
            {
                _currentLockId = null;
                _isLocked = false;
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

    private void TryGrantNext()
    {
        LockRequest? next = null;

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
                break;
            }

            if (next == null) return;
            _isLocked = true;
        }

        _ = GrantRequestAsync(next);
    }

    private async Task GrantRequestAsync(LockRequest request)
    {
        try
        {
            _currentTransaction = await _sharedContext.Database.BeginTransactionAsync().ConfigureAwait(false);
            var lockId = Guid.NewGuid();

            lock (_queueLock)
            {
                _currentLockId = lockId;
            }

            request.SetResult(lockId);
            _logger.LogInformation("Granted database lock {LockId}.", lockId);
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
            request.CancellationRegistration?.Dispose();
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

    private bool TryRemoveRequest(LockRequest request)
    {
        if (request == null) return false;
        if (_lockQueue.Count == 0) return false;

        var found = false;
        var buffer = new List<LockRequest>(_lockQueue.Count);
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
    }

    private sealed class LockRequest
    {
        private readonly TaskCompletionSource<Guid> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Guid> Task => _tcs.Task;
        public CancellationTokenRegistration? CancellationRegistration;
        public bool IsCanceled { get; private set; }

        public LockRequest(CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled && cancellationToken.IsCancellationRequested)
            {
                IsCanceled = true;
                _tcs.TrySetCanceled(cancellationToken);
            }
        }

        public void SetResult(Guid id) => _tcs.TrySetResult(id);
        public void SetCanceled()
        {
            IsCanceled = true;
            _tcs.TrySetCanceled();
        }
        public void SetException(Exception ex) => _tcs.TrySetException(ex);
    }
}
