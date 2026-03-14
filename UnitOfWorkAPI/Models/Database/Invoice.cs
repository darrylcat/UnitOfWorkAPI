using System.ComponentModel.DataAnnotations;

namespace UnitOfWorkAPI.Models.Database;

public class Invoice : BaseRecord
{
    [Required]
    public int CustomerId { get; set; }
    public virtual Customer Customer { get; set; }
    [Required]
    public DateTime DateOfSale { get; set; }
    public DateTime? DateDispatched { get; set; }
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Gross { get; set; }

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; }
}
