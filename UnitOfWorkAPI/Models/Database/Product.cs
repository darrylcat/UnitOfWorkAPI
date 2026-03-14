using System.ComponentModel.DataAnnotations;

namespace UnitOfWorkAPI.Models.Database;

public class Product : BaseRecord
{
    [Required]
    [MaxLength(128)]
    public string Plu { get; set; }
    [Required]
    [MaxLength(128)]
    public string ShortDescription { get; set; }
    [Required]
    [MaxLength(4000)]
    public string FullDescription { get; set; }

    [Required]
    public int PackSize { get; set; }
    [Required]
    public decimal Cost { get; set; }
    [Required]
    public decimal Price { get; set; }
    [Required]
    public int QtyInStock { get; set; }

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; }
}
