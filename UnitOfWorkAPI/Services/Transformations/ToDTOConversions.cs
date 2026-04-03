using UnitOfWorkAPI.Models.Database;
using UnitOfWorkAPI.Models.DTOs.Data;

namespace UnitOfWorkAPI.Services.Transformations;

public static class ToDTOConversions
{
    public static UserDetailDTO ToDTO(this UserDetail entity)
    {
        return new UserDetailDTO()
        {
            Id = entity.Id,
            UpdatedById = entity.UpdatedById,
            UpdatedByName = entity.UpdatedBy?.UserName ?? string.Empty,
            UpdatedTime = entity.UpdatedTime,
            FirstName = entity.FirstName,
            LastName = entity.LastName,
            Active = entity.Active,
            Email = entity.Email,
            UserName = entity.UserName,
        };
    }
}
