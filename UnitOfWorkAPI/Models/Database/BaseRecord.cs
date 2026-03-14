using System.ComponentModel.DataAnnotations;

namespace UnitOfWorkAPI.Models.Database;

public abstract class BaseRecord
{
    [Key()]
    public int Id { get; set; }
    [Required]
    public DateTime UpdatedTime { get; set; }
    [Required]
    public int UpdatedById { get; set; }
    public virtual UserDetail UpdatedBy { get; set; }
}
