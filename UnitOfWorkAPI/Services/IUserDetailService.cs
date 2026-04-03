using UnitOfWorkAPI.Models.DTOs.Data;
using UnitOfWorkAPI.Models.DTOs.Requests;
using UnitOfWorkAPI.Models.DTOs.Responses;

namespace UnitOfWorkAPI.Services;

public interface IUserDetailService
{
    Task<UserDetailPagedQueryResult> GetPagedQuery(UserDetailPagedQuery userDetailPagedQuery, CancellationToken cancellationToken);

    Task<UserDetailDTO> GetUser(int  id, CancellationToken cancellationToken);
}
