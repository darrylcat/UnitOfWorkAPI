using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace UnitOfWorkAPI.Models.DTOs.Requests;

public class BasePagedQuery
{
    /// <summary>
    /// Text you are looking form
    /// </summary>
    [JsonPropertyName("searchFor")]
    public string? SearchFor { get; set; }
    /// <summary>
    /// Page number you are looking to retieve, i.e. skip Size * Page records
    /// </summary>
    [JsonPropertyName("page")]
    [Range(0, int.MaxValue, ErrorMessage = "Page must be 0 or greater.")]
    public int Page { get; set; } = 0;
    /// <summary>
    /// Number of records to retrieve, min 10 to 255
    /// </summary>
    [JsonPropertyName("size")]
    [Range(10, 255, ErrorMessage = "Size must be between 10 and 255.")]
    public int Size { get; set; } = 255;
}
