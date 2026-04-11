using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace UnitOfWorkAPI.Models.Database;

public class InvoiceItem : BaseRecord
{
    [Required]
    public int InvoiceId { get; set; }
    public virtual Invoice Invoice { get; set; }

    [Required]
    public int ProductId { get; set; }
    public virtual Product Product { get; set; }
    public int Qty { get; set; }
    [Precision(18, 2)]
    public decimal Price { get; set; }
}
