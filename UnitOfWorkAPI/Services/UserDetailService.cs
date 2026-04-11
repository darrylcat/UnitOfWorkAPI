using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UnitOfWorkAPI.Models.Database;
using UnitOfWorkAPI.Models.DTOs.Data;
using UnitOfWorkAPI.Models.DTOs.Requests;
using UnitOfWorkAPI.Models.DTOs.Responses;
using UnitOfWorkAPI.Services.Transformations;

namespace UnitOfWorkAPI.Services;

public class UserDetailService : IUserDetailService
{
    private readonly ILogger<UserDetailService> logger;
    private readonly IUnitOfWorkService unitOfWorkService;

    public UserDetailService(ILogger<UserDetailService> logger, IUnitOfWorkService unitOfWorkService)
    {
        this.logger = logger;
        this.unitOfWorkService = unitOfWorkService;
    }

    public async Task<UserDetailPagedQueryResult> GetPagedQuery(UserDetailPagedQuery userDetailPagedQuery, CancellationToken cancellationToken)
    {
        var result = new UserDetailPagedQueryResult();
        bool searchForIsNull = string.IsNullOrEmpty(userDetailPagedQuery.SearchFor);
        string normalisedSearchFor = userDetailPagedQuery.SearchFor?.Trim().ToLower() ?? string.Empty;
        try
        {
            var results = await unitOfWorkService.SelectAsync<UserDetail>(
                c => c.UserDetails.AsQueryable()
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

    public async Task<UserDetailDTO> GetUser(int id, CancellationToken cancellationToken)
    {
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
    }

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
                                                    .AsQueryable()
                                                    .AsNoTracking()
                                                    .Where(x => x.Id == id)
                                                    .Take(1), cancellationToken);
    }
}
