using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
    [Precision(18, 2)]
    public decimal Net { get; set; }
    [Precision(18, 2)]
    public decimal Vat { get; set; }
    [Precision(18, 2)]
    public decimal Gross { get; set; }

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new HashSet<InvoiceItem>();
}
