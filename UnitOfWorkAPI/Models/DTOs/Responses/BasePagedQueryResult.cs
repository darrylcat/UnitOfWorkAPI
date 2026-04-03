using System.Text.Json.Serialization;
using UnitOfWorkAPI.Models.DTOs.Data;
using UnitOfWorkAPI.Models.DTOs.Requests;

namespace UnitOfWorkAPI.Models.DTOs.Responses;

public class BasePagedQueryResult<Q,DTO>
{
    [JsonPropertyName("query")]
    public Q UserDetailPagedQuery { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("data")]
    public IEnumerable<DTO> Data { get; set; }

    [JsonPropertyName("errorMessages")]
    public IEnumerable<string> ErrorMessages { get; set; }

    public BasePagedQueryResult()
    {
        this.Data = new List<DTO>();
        this.ErrorMessages = new List<string>();
    }
}
