using Microsoft.EntityFrameworkCore;
using Store.Api.Domain;

namespace Store.Api.Infrastructure;

public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StoreOrder> Orders => Set<StoreOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateSqliteConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateSqliteConcurrencyTokens();
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().HasIndex(x => x.Email).IsUnique();
        model.Entity<User>().Property(x => x.Email).HasMaxLength(320);
        model.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        model.Entity<Product>().Property(x => x.Sku).HasMaxLength(64);
        model.Entity<Product>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<Product>().Property(x => x.Price).HasPrecision(18, 2);
        ConfigureConcurrencyToken(model.Entity<Product>().Property(x => x.RowVersion));
        model.Entity<StoreOrder>().HasIndex(x => new { x.CustomerId, x.IdempotencyKey }).IsUnique();
        model.Entity<StoreOrder>().Property(x => x.SubTotal).HasPrecision(18, 2);
        model.Entity<StoreOrder>().Property(x => x.Discount).HasPrecision(18, 2);
        model.Entity<StoreOrder>().Property(x => x.Total).HasPrecision(18, 2);
        model.Entity<OrderItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        model.Entity<OrderItem>().HasOne(x => x.Product).WithMany(x => x.OrderItems)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<RefreshToken>().Property(x => x.TokenHash).HasMaxLength(64);
        ConfigureConcurrencyToken(model.Entity<RefreshToken>().Property(x => x.RowVersion));
    }

    private void ConfigureConcurrencyToken(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<byte[]> property)
    {
        if (Database.IsSqlite())
            property.IsConcurrencyToken();
        else
            property.IsRowVersion();
    }

    private void UpdateSqliteConcurrencyTokens()
    {
        if (!Database.IsSqlite()) return;

        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State == EntityState.Modified &&
                                     entry.Entity is Product or RefreshToken))
        {
            entry.Property(nameof(Product.RowVersion)).CurrentValue =
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        }
    }
}
