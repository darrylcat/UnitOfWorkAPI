using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnitOfWorkAPI.Models.Database;

namespace UnitOfWorkAPI.Services;

public interface IUnitOfWorkService : IDisposable
{
    Task<Guid> GetDatabaseLockAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<T>> SelectAsync<T>(Func<UOWContext, IQueryable<T>> queryFactory, CancellationToken cancellationToken = default)
        where T : class;

    Task<int> InsertAsync<T>(IEnumerable<T> entities, Guid lockId, CancellationToken cancellationToken = default)
        where T : class;

    Task<int> UpdateAsync<T>(IEnumerable<T> entities, Guid lockId, CancellationToken cancellationToken = default)
        where T : class;

    Task<int> DeleteAsync<T>(IEnumerable<T> entities, Guid lockId, CancellationToken cancellationToken = default)
        where T : class;

    Task ReleaseDataLockAsync(Guid lockId, DbTransactionOption option, CancellationToken cancellationToken = default);
}