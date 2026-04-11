using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
    [Precision(18,2)]
    public decimal CreditLimit { get; set; }
    [Precision(18, 2)]
    public decimal Balance { get; set; }
}
