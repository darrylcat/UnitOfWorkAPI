using Microsoft.EntityFrameworkCore;

namespace UnitOfWorkAPI.Models.Database;

public class UOWContext : DbContext
{

    public DbSet<Customer> Customers { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<InvoiceItem> InvoicesItems { get; set; }
    public DbSet<UserDetail> UserDetails { get; set; }
}
