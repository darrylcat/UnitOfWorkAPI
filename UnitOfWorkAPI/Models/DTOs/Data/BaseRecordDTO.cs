using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using UnitOfWorkAPI.Models.Database;

namespace UnitOfWorkAPI.Models.DTOs.Data;

public class BaseRecordDTO
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("updatedTime")]
    public DateTime UpdatedTime { get; set; }
    [JsonPropertyName("updatedById")]
    public int UpdatedById { get; set; }
    [JsonPropertyName("updatedByName")]
    public string UpdatedByName { get; set; }

}
