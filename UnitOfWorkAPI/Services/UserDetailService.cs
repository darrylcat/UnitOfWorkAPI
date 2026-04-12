using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UnitOfWorkAPI.Models.Database;
using UnitOfWorkAPI.Models.DTOs.Data;
using UnitOfWorkAPI.Models.DTOs.Requests;
using UnitOfWorkAPI.Models.DTOs.Responses;
using UnitOfWorkAPI.Services.Transformations;

namespace UnitOfWorkAPI.Services;

/// <summary>
/// Provides operations for querying, creating, and updating user detail records with support for database locking and
/// paging.
/// </summary>
public class UserDetailService : IUserDetailService
{
    private readonly ILogger<UserDetailService> logger;
    private readonly IUnitOfWorkService unitOfWorkService;

    public UserDetailService(ILogger<UserDetailService> logger, IUnitOfWorkService unitOfWorkService)
    {
        this.logger = logger;
        this.unitOfWorkService = unitOfWorkService;
    }

    /// <summary>
    /// Searches the UserDetail table for records matching search criteria.
    /// Doesn't use a database lock, so records returned may not match any changed
    /// since the current transaction completes/fails
    /// </summary>
    /// <param name="userDetailPagedQuery">parameters used to search the UserDetail table</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<UserDetailPagedQueryResult> GetPagedQuery(UserDetailPagedQuery userDetailPagedQuery, CancellationToken cancellationToken)
    {
        var result = new UserDetailPagedQueryResult();
        bool searchForIsNull = string.IsNullOrEmpty(userDetailPagedQuery.SearchFor);
        string normalisedSearchFor = userDetailPagedQuery.SearchFor?.Trim().ToLower() ?? string.Empty;
        try
        {
            var results = await unitOfWorkService.SelectAsync<UserDetail>(
                c => c.UserDetails
                .AsNoTracking()
                .AsQueryable()
                .Where(x => searchForIsNull ||  
                    x.UserName.Trim().ToLower().Contains(normalisedSearchFor) || 
                    x.FirstName.Trim().ToLower().Contains(normalisedSearchFor) ||
                    x.LastName.Trim().ToLower().Contains(normalisedSearchFor)
                )
                .OrderBy(o => o.UserName)
                .Skip(userDetailPagedQuery.Page * userDetailPagedQuery.Size)
                .Take(userDetailPagedQuery.Size)
                , cancellationToken);
            result.Data = results.Select(s => s.ToDTO()).AsEnumerable();
        }
        catch (Exception ex) {
            result.ErrorMessages.Add(ex.Message);
            logger.LogError(ex.Message);
        }
        finally
        {
            result.Total = result.Data.Count();
        }
        return result;
    }

    /// <summary>
    /// Fetches a single UserDetail record. 
    /// Uses a Database lock to ensure it gets a record from the DB without
    /// changes being in progress
    /// </summary>
    /// <param name="id">UserDetail.Id of record to retrieve</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="KeyNotFoundException"></exception>
    public async Task<UserDetailDTO> GetUser(int id, CancellationToken cancellationToken)
    {
        bool released = false;
        var lockId = await unitOfWorkService.GetDatabaseLockAsync();
        try
        {
            var entities = await Find(id, cancellationToken);
            if(entities == null || !entities.Any())
            {
                throw new KeyNotFoundException($"Unable to find user detail with Id of {id}");
            }
            return entities.First().ToDTO();
        }
        catch(Exception ex)
        {
            logger.LogError(ex.Message);
            throw;
        }
        finally
        {
            await unitOfWorkService.ReleaseDataLockAsync(lockId, DbTransactionOption.Rollback, cancellationToken);
            released = true;
        }
    }

    /// <summary>
    /// Creates a new user detail record asynchronously.
    /// </summary>
    /// <param name="entity">The user detail data to create.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created user detail.</returns>
    public async Task<UserDetailDTO> Create(UserDetailDTO entity, CancellationToken cancellationToken)
    {
        bool released = false;
        var lockId = await unitOfWorkService.GetDatabaseLockAsync();
        try
        {
            var entities = new List<UserDetail>();
            entities.Add(new UserDetail()
            {
                Active = entity.Active,
                Email = entity.Email,
                FirstName = entity.FirstName,
                LastName = entity.LastName,
                UserName = entity.UserName,
                UpdatedById = entity.UpdatedById,
                UpdatedTime = DateTime.UtcNow
            });
            var changesMade = await unitOfWorkService.InsertAsync(entities, lockId, cancellationToken);

            if(changesMade != 1)
            {
                throw new Exception("Unable to create new User Detail record");
            }

            await unitOfWorkService.ReleaseDataLockAsync(lockId, DbTransactionOption.Commit, cancellationToken);
            released = true;
            return entities.First().ToDTO();
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            throw;
        }
        finally
        {
            if (!released)
            {
                await unitOfWorkService.ReleaseDataLockAsync(lockId, DbTransactionOption.Rollback, cancellationToken);
            }
        }
    }


    public async Task<Boolean> Update(int id, UserDetailDTO dto, CancellationToken cancellationToken)
    {
        bool released = false;
        var lockId = await unitOfWorkService.GetDatabaseLockAsync();
        try
        {
            var entities = await Find(id, cancellationToken);

            if (entities == null || ! entities.Any())
            {
                throw new KeyNotFoundException($"Unable to find existing user detail record {id}");
            }

            var entity = entities.First();
            entity.UpdatedById = dto.UpdatedById;
            entity.UpdatedTime = DateTime.UtcNow;
            entity.UserName = dto.UserName;
            entity.LastName = dto.LastName;
            entity.FirstName = dto.FirstName;
            entity.Active = dto.Active;
            entity.Email = dto.Email;

            var result = await unitOfWorkService.UpdateAsync(entities, lockId, cancellationToken);
            if(result != 1)
            {
                throw new Exception($"Unable to update user detail record {id}");
            }

            await Task.Delay(50 * 1000);

            await unitOfWorkService.ReleaseDataLockAsync(lockId, DbTransactionOption.Commit, cancellationToken);
            released = true;
            return true;
        }
        catch (Exception ex) {
            logger.LogError(ex.Message);
            return false;
        }
        finally
        {
            if (!released)
            {
                await unitOfWorkService.ReleaseDataLockAsync(lockId, DbTransactionOption.Rollback, cancellationToken);
            }
        }
    }

    private Task<IEnumerable<UserDetail>> Find(int id, CancellationToken cancellationToken)
    {
        return unitOfWorkService.SelectAsync(c => c.UserDetails
                                                    .AsNoTracking()
                                                    .AsQueryable()
                                                    .Where(x => x.Id == id)
                                                    .Take(1), cancellationToken);
    }
}
