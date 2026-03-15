// UnitOfWorkAPI/Services/DefaultLockRequestFactory.cs
using System.Threading;

namespace UnitOfWorkAPI.Services;

public sealed class DefaultLockRequestFactory : ILockRequestFactory
{
    public ILockRequest Create(CancellationToken cancellationToken) => new DefaultLockRequest(cancellationToken);
}