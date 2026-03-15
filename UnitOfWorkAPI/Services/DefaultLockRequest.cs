// UnitOfWorkAPI/Services/DefaultLockRequest.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace UnitOfWorkAPI.Services;

internal sealed class DefaultLockRequest : ILockRequest
{
    private readonly TaskCompletionSource<Guid> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<Guid> Task => _tcs.Task;
    public CancellationToken Token { get; }
    public CancellationTokenRegistration? CancellationRegistration { get; set; }
    public bool IsCanceled { get; private set; }
    public bool IsGranting { get; set; }

    public DefaultLockRequest(CancellationToken cancellationToken)
    {
        Token = cancellationToken;
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