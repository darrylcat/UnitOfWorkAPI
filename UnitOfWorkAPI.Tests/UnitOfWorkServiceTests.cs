using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UnitOfWorkAPI.Models.Database;
using UnitOfWorkAPI.Services;
using Xunit;

namespace UnitOfWorkAPI.Tests;

public sealed class UnitOfWorkServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContextFactory _factory;

    public UnitOfWorkServiceTests()
    {
        // Create a shared in-memory SQLite connection so multiple contexts see the same DB.
        _connection = new SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<UOWContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new TestDbContextFactory(options);

        // Ensure schema exists and seed a default user for FK requirements.
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        // add a UserDetail used as UpdatedBy for entities
        if (!ctx.UserDetails.Any())
        {
            var user = new UserDetail
            {
                Id = 1,
                UserName = "seed",
                FirstName = "Seed",
                LastName = "User",
                Email = "seed@example.com",
                Active = true,
                UpdatedTime = DateTime.UtcNow,
                UpdatedById = 1 // self reference placeholder (not enforced on creation)
            };

            ctx.UserDetails.Add(user);
            ctx.SaveChanges();
        }
    }

    [Fact]
    public async Task InsertCommit_PersistsData()
    {
        var logger = NullLogger<UnitOfWorkService>.Instance;
        var factory = new TestLockRequestFactory();
        using var sut = new UnitOfWorkService(_factory, logger, transactionStarter: null, lockRequestFactory: factory);

        // get an existing user id to satisfy UpdatedBy FK
        int userId;
        using (var ctx = _factory.CreateDbContext())
        {
            userId = ctx.UserDetails.Select(u => u.Id).First();
        }

        var lockId = await sut.GetDatabaseLockAsync();

        var cust = new Customer
        {
            Businessname = "Test Business",
            PostCode = "12345",
            CreditLimit = 1000m,
            Balance = 0m,
            UpdatedTime = DateTime.UtcNow,
            UpdatedById = userId
        };

        var inserted = await sut.InsertAsync(new[] { cust }, lockId);
        Assert.Equal(1, inserted);

        await sut.ReleaseDataLockAsync(lockId, DbTransactionOption.Commit);

        // verify persisted using new context
        using var verifyCtx = _factory.CreateDbContext();
        var count = verifyCtx.Customers.Count();
        Assert.Equal(1, count);
        var persisted = verifyCtx.Customers.First();
        Assert.Equal("Test Business", persisted.Businessname);
    }

    [Fact]
    public async Task Selects_CanRunConcurrently()
    {
        var logger = NullLogger<UnitOfWorkService>.Instance;
        var factory = new TestLockRequestFactory();
        using var sut = new UnitOfWorkService(_factory, logger, transactionStarter: null, lockRequestFactory: factory);

        // seed multiple customers
        using (var ctx = _factory.CreateDbContext())
        {
            int userId = ctx.UserDetails.Select(u => u.Id).First();
            for (int i = 0; i < 10; i++)
            {
                ctx.Customers.Add(new Customer
                {
                    Businessname = $"B{i}",
                    PostCode = "00000",
                    UpdatedTime = DateTime.UtcNow,
                    UpdatedById = userId
                });
            }
            ctx.SaveChanges();
        }

        // run many concurrent selects
        var tasks = Enumerable.Range(0, 8).Select(_ =>
            sut.SelectAsync<Customer>(c => c.Customers.AsQueryable())).ToArray();

        await Task.WhenAll(tasks);

        foreach (var t in tasks)
        {
            Assert.Equal(10, t.Result.Count());
        }
    }

    [Fact]
    public async Task LockRequests_AreFifo()
    {
        var logger = NullLogger<UnitOfWorkService>.Instance;
        var factory = new TestLockRequestFactory();
        using var sut = new UnitOfWorkService(_factory, logger, transactionStarter: null, lockRequestFactory: factory);

        var t1 = sut.GetDatabaseLockAsync();
        var t2 = sut.GetDatabaseLockAsync();

        // First should be granted before second
        var completed = await Task.WhenAny(t1, t2);
        Assert.Equal(t1, completed);

        var lock1 = await t1;
        await sut.ReleaseDataLockAsync(lock1, DbTransactionOption.Commit);

        var lock2 = await t2; // should now be granted
        Assert.NotEqual(lock1, lock2);

        await sut.ReleaseDataLockAsync(lock2, DbTransactionOption.Commit);
    }

    [Fact]
    public async Task CanceledRequest_IsRemovedFromQueue()
    {
        var logger = NullLogger<UnitOfWorkService>.Instance;
        var factory = new TestLockRequestFactory();
        using var sut = new UnitOfWorkService(_factory, logger, transactionStarter: null, lockRequestFactory: factory);

        var cts = new CancellationTokenSource();
        var t1 = sut.GetDatabaseLockAsync(cts.Token);
        var t2 = sut.GetDatabaseLockAsync();

        // Cancel the first request before it is granted
        cts.Cancel();

        try
        {
            // Expect t1 to be cancelled
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await t1);

        }
        catch (TaskCanceledException tce)
        {
            Console.WriteLine(tce.Message.ToString());
        }
        catch (OperationCanceledException ex) { 
            Console.WriteLine(ex.Message.ToString());
        }

        // t2 should still be granted
        var lock2 = await t2;
        await sut.ReleaseDataLockAsync(lock2, DbTransactionOption.Commit);
    }

    // New test demonstrating deterministic delayed grant using mockable LockRequest.
    [Fact]
    public async Task DelayedGrant_AllowsTestControlledGrantAndCancellation()
    {
        var logger = NullLogger<UnitOfWorkService>. Instance;
        var factory = new TestLockRequestFactory();
        using var sut = new UnitOfWorkService(_factory, logger, transactionStarter: null, lockRequestFactory: factory);

        var cts = new CancellationTokenSource();
        var t1 = sut.GetDatabaseLockAsync(cts.Token);
        var t2 = sut.GetDatabaseLockAsync();

        // Wait until the service has created the first request (it may create it synchronously).
        var req1 = await factory.WaitForRequestAsync();

        // Make sure grant is delayed by not allowing SetResult to complete yet.
        // Cancel while the request is in the delayed-grant state.
        cts.Cancel();

        // Allow the grant to proceed (GrantRequestAsync will call SetResult; our mock will complete only after this signal)
        req1.AllowGrant();

        // Expect t1 to reflect cancellation
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await t1);

        // t2 should still be granted
        var lock2 = await t2;
        await sut.ReleaseDataLockAsync(lock2, DbTransactionOption.Commit);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    // Minimal IDbContextFactory implementation for tests
    private sealed class TestDbContextFactory : IDbContextFactory<UOWContext>, IDisposable
    {
        private readonly DbContextOptions<UOWContext> _options;

        public TestDbContextFactory(DbContextOptions<UOWContext> options)
        {
            _options = options;
        }

        public UOWContext CreateDbContext()
        {
            return new UOWContext(_options);
        }

        public void Dispose()
        {
            // nothing to dispose here; underlying connection closed by test fixture
        }
    }

    // Test helpers: a factory and controllable mock LockRequest so tests can simulate delays and observe cancellation deterministically.
    private sealed class TestLockRequestFactory : ILockRequestFactory
    {
        private readonly ConcurrentQueue<MockLockRequest> _created = new();
        private readonly TaskCompletionSource<bool> _hasRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ILockRequest Create(CancellationToken cancellationToken)
        {
            var req = new MockLockRequest(cancellationToken);
            _created.Enqueue(req);
            _hasRequest.TrySetResult(true);
            return req;
        }

        public async Task<MockLockRequest> WaitForRequestAsync(int timeoutMs = 1000)
        {
            // If queue already has an item, return it.
            if (_created.TryDequeue(out var existing)) return existing;

            // Wait for a creation signal (simple timeout to avoid test hangs).
            var completed = await Task.WhenAny(_hasRequest.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);

            if (!_created.TryDequeue(out existing))
            {
                throw new TimeoutException("Timed out waiting for a LockRequest to be created by the SUT.");
            }

            return existing;
        }
    }

    private sealed class MockLockRequest : ILockRequest
    {
        private readonly TaskCompletionSource<Guid> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowGrant = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration? _selfRegistration;

        public Task<Guid> Task => _tcs.Task;
        public CancellationToken Token { get; }
        public CancellationTokenRegistration? CancellationRegistration { get; set; }
        public bool IsCanceled { get; private set; }
        public bool IsGranting { get; set; }

        public MockLockRequest(CancellationToken cancellationToken)
        {
            Token = cancellationToken;

            // Also observe token directly so tests are deterministic regardless of SUT registration timing.
            if (Token.CanBeCanceled)
            {
                _selfRegistration = Token.Register(() =>
                {
                    IsCanceled = true;
                    _tcs.TrySetCanceled(Token);
                }, useSynchronizationContext: false);
            }

            // If token already cancelled, reflect that immediately.
            if (Token.CanBeCanceled && Token.IsCancellationRequested)
            {
                IsCanceled = true;
                _tcs.TrySetCanceled(Token);
            }
        }

        // Called by UnitOfWorkService to complete grant. We defer completion until test allows it.
        public void SetResult(Guid id)
        {
            // Run completion as a continuation of the allow-grant task to avoid background-task exceptions
            _allowGrant.Task.ContinueWith(t =>
            {
                try
                {
                    if (IsCanceled)
                    {
                        _tcs.TrySetCanceled(Token);
                    }
                    else
                    {
                        _tcs.TrySetResult(id);
                    }
                }
                catch (Exception ex)
                {
                    // Ensure any exception is observed and placed on the tcs so the test runner doesn't break unexpectedly.
                    _tcs.TrySetException(ex);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.DenyChildAttach,
            TaskScheduler.Default);
        }

        public void SetCanceled()
        {
            IsCanceled = true;
            // Dispose the self-registration so we don't leave a dangling registration if cancellation is being applied from SUT.
            try
            {
                _selfRegistration?.Dispose();
            }
            catch { /* best-effort */ }

            // Preserve the original token when signaling cancellation so awaiters see the same cancellation token information.
            if (Token.CanBeCanceled)
            {
                _tcs.TrySetCanceled(Token);
            }
            else
            {
                _tcs.TrySetCanceled();
            }
        }

        public void SetException(Exception ex) => _tcs.TrySetException(ex);

        public void AllowGrant() => _allowGrant.TrySetResult(true);
    }

    private readonly LinkedList<ILockRequest> _lockList = new();
    private readonly Dictionary<ILockRequest, LinkedListNode<ILockRequest>> _nodeByRequest = new();

    private void EnqueueRequest(ILockRequest req)
    {
        var node = _lockList.AddLast(req);
        _nodeByRequest[req] = node;
    }

    private bool TryRemoveRequest(ILockRequest req)
    {
        if (_nodeByRequest.TryGetValue(req, out var node))
        {
            _lockList.Remove(node);
            _nodeByRequest.Remove(req);
            return true;
        }
        return false;
    }
}