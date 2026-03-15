// UnitOfWorkAPI/Services/ILockRequestFactory.cs
using System.Threading;

namespace UnitOfWorkAPI.Services;

public interface ILockRequestFactory
{
    ILockRequest Create(CancellationToken cancellationToken);
}