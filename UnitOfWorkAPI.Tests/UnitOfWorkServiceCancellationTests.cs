using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using UnitOfWorkAPI.Services;
using Xunit;

namespace UnitOfWorkAPI.Tests;

public class UnitOfWorkServiceCancellationTests
{
    // Fake transaction that records whether CommitAsync / RollbackAsync were invoked.
    private class FakeTransaction : IDbContextTransaction
    {
        public bool CommitCalled { get; private set; }
        public bool RollbackCalled { get; private set; }

        public Guid TransactionId { get; } = Guid.NewGuid();

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalled = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCalled = true;
            return Task.CompletedTask;
        }

        public void Commit() => CommitCalled = true;
        public void Rollback() => RollbackCalled = true;

        public void Dispose() { }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task EnqueueBeforeCancel_RequestIsCanceledAndRemoved()
    {
        var logger = NullLogger<UnitOfWorkService>.Instance;

        // Immediate transaction starter to avoid blocking grant for other requests.
        var tx = new FakeTransaction();
        Func<CancellationToken, Task<IDbContextTransaction>> starter = _ => Task.FromResult<IDbContextTransaction>(tx);

        using var sut = new UnitOfWorkService(logger, starter);

        var cts = new CancellationTokenSource();
        var t1 = sut.GetDatabaseLockAsync(cts.Token);
        var t2 = sut.GetDatabaseLockAsync();

        // Cancel the first request before it's processed
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await t1);

        // t2 should still be granted
        var lock2 = await t2;
        await sut.ReleaseDataLockAsync(lock2, DbTransactionOption.Commit);
    }

    [Fact]
    public async Task CancelWhileGranting_RequestIsCanceledAndNextGranted()
    {
        var logger = NullLogger<UnitOfWorkService>.Instance;

        // Transaction starter that delays and honors the provided token.
        Func<CancellationToken, Task<IDbContextTransaction>> starter = async token =>
        {
            // Delay longer than the cancellation we will trigger.
            await Task.Delay(1000, token).ConfigureAwait(false);
            return new FakeTransaction();
        };

        using var sut = new UnitOfWorkService(logger, starter);

        var cts = new CancellationTokenSource();
        var t1 = sut.GetDatabaseLockAsync(cts.Token);
        var t2 = sut.GetDatabaseLockAsync();

        // Allow grant to begin and be in-flight, then cancel t1
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await t1);

        // t2 should be granted after t1 cancel/unwind
        var lock2 = await t2;
        await sut.ReleaseDataLockAsync(lock2, DbTransactionOption.Commit);
    }

    [Fact]
    public async Task HolderCancellation_RollsBackAndReleasesLock()
    {
        var logger = NullLogger<UnitOfWorkService>.Instance;

        // We'll capture the transaction instance granted so we can assert rollback was called.
        FakeTransaction? captured = null;
        Func<CancellationToken, Task<IDbContextTransaction>> starter = token =>
        {
            captured = new FakeTransaction();
            return Task.FromResult<IDbContextTransaction>(captured);
        };

        using var sut = new UnitOfWorkService(logger, starter);

        var cts = new CancellationTokenSource();
        var t1 = sut.GetDatabaseLockAsync(cts.Token);

        var lockId = await t1;

        // Cancel the holder before it calls ReleaseDataLockAsync -> should trigger automatic rollback.
        cts.Cancel();

        // Give the cancellation handler a brief moment to run (it's fire-and-forget inside the service).
        await Task.Delay(50);

        Assert.NotNull(captured);
        Assert.True(captured!.RollbackCalled, "Transaction should have been rolled back when holder cancelled.");

        // After rollback the lock should be released and another requester can obtain lock.
        var t2 = sut.GetDatabaseLockAsync();
        var lock2 = await t2;
        await sut.ReleaseDataLockAsync(lock2, DbTransactionOption.Commit);
    }
}