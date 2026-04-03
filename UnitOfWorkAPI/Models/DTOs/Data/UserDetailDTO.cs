using System.Text.Json.Serialization;

namespace UnitOfWorkAPI.Models.DTOs.Data;

public class UserDetailDTO : BaseRecordDTO
{
    [JsonPropertyName("userName")]
    public string UserName { get; set; }
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; }
    [JsonPropertyName("lastName")]
    public string LastName { get; set; }
    [JsonPropertyName("email")]
    public string Email { get; set; }
    [JsonPropertyName("active")]
    public bool Active { get; set; }
}
