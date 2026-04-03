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
        try
        {
            var results = await unitOfWorkService.SelectAsync<UserDetail>(
                c => c.UserDetails.AsQueryable()
                .Where(x => x.UserName.Contains(userDetailPagedQuery.SearchFor.Trim()))
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
            var entities = await unitOfWorkService.SelectAsync<UserDetail>(c => c.UserDetails
            .AsQueryable()
            .Where(x => x.Id == id)
            .Take(1)
            , cancellationToken);
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
}
