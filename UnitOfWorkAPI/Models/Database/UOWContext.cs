using Microsoft.EntityFrameworkCore;

namespace UnitOfWorkAPI.Models.Database;

public class UOWContext : DbContext
{
    public UOWContext(DbContextOptions<UOWContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceItem> InvoicesItems { get; set; } = null!;
    public DbSet<UserDetail> UserDetails { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Map each concrete type to its table explicitly (optional, but makes intent clear)
        modelBuilder.Entity<UserDetail>().ToTable("UserDetails");
        modelBuilder.Entity<Customer>().ToTable("Customers");
        modelBuilder.Entity<Product>().ToTable("Products");
        modelBuilder.Entity<Invoice>().ToTable("Invoices");
        modelBuilder.Entity<InvoiceItem>().ToTable("InvoiceItems");

        // Invoice -> Customer (one-to-many)
        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.Customer)
            .WithMany()
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // InvoiceItem -> Invoice (many-to-one)
        modelBuilder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Invoice)
            .WithMany(i => i.InvoiceItems)
            .HasForeignKey(ii => ii.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // InvoiceItem -> Product (many-to-one)
        modelBuilder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Product)
            .WithMany(p => p.InvoiceItems)
            .HasForeignKey(ii => ii.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure UpdatedBy relationships for derived types to reference UserDetail (no inverse collections)
        modelBuilder.Entity<Customer>().HasOne(c => c.UpdatedBy).WithMany().HasForeignKey("UpdatedById").OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Product>().HasOne(p => p.UpdatedBy).WithMany().HasForeignKey("UpdatedById").OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>().HasOne(i => i.UpdatedBy).WithMany().HasForeignKey("UpdatedById").OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InvoiceItem>().HasOne(ii => ii.UpdatedBy).WithMany().HasForeignKey("UpdatedById").OnDelete(DeleteBehavior.Restrict);

        // --- Fix: ensure UserDetail self-reference does NOT cascade delete ---
        modelBuilder.Entity<UserDetail>().HasOne(ud => ud.UpdatedBy).WithMany().HasForeignKey("UpdatedById").OnDelete(DeleteBehavior.Restrict);

        base.OnModelCreating(modelBuilder);
    }
}
