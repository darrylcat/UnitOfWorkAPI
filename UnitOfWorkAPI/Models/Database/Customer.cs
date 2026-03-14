using System.ComponentModel.DataAnnotations;

namespace UnitOfWorkAPI.Models.Database;

public class Customer : BaseRecord
{
    [Required]
    [MaxLength(255)]
    public string Businessname { get; set; }
    [MaxLength(255)]
    public string Address1 { get; set; }
    [MaxLength(255)]
    public string Address2 { get; set; }
    [MaxLength(255)]
    public string Address3 { get; set; }
    [MaxLength(255)]
    public string Address4 { get; set; }
    [Required]
    [MaxLength(10)]
    public string PostCode { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal Balance { get; set; }
}
