// UnitOfWorkAPI/Services/ILockRequest.cs
using System;
using System.Threading;

namespace UnitOfWorkAPI.Services;

public interface ILockRequest
{
    Task<Guid> Task { get; }
    CancellationToken Token { get; }
    CancellationTokenRegistration? CancellationRegistration { get; set; }
    bool IsCanceled { get; }
    bool IsGranting { get; set; }

    void SetResult(Guid id);
    void SetCanceled();
    void SetException(Exception ex);
}