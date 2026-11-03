using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Store.Api.Domain;
using Store.Api.Infrastructure;

#nullable disable

namespace Store.Api.Infrastructure.Migrations;

[DbContext(typeof(StoreDbContext))]
public sealed class StoreDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.11");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 128);

        modelBuilder.Entity<User>(b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            b.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("nvarchar(320)");
            b.Property<string>("FullName").IsRequired().HasColumnType("nvarchar(max)");
            b.Property<string>("PasswordHash").IsRequired().HasColumnType("nvarchar(max)");
            b.Property<int>("Role").HasColumnType("int");
            b.HasKey("Id");
            b.HasIndex("Email").IsUnique();
            b.ToTable("Users");
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            b.Property<bool>("IsActive").HasColumnType("bit");
            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("nvarchar(200)");
            b.Property<decimal>("Price").HasPrecision(18, 2).HasColumnType("decimal(18,2)");
            b.Property<byte[]>("RowVersion").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.Property<int>("StockQuantity").HasColumnType("int");
            b.Property<string>("Sku").IsRequired().HasMaxLength(64).HasColumnType("nvarchar(64)");
            b.HasKey("Id");
            b.HasIndex("Sku").IsUnique();
            b.ToTable("Products");
        });

        modelBuilder.Entity<StoreOrder>(b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            b.Property<DateTime>("CreatedAt").HasColumnType("datetime2");
            b.Property<int>("CustomerId").HasColumnType("int");
            b.Property<string>("IdempotencyKey").IsRequired().HasColumnType("nvarchar(450)");
            b.Property<decimal>("Discount").HasPrecision(18, 2).HasColumnType("decimal(18,2)");
            b.Property<decimal>("SubTotal").HasPrecision(18, 2).HasColumnType("decimal(18,2)");
            b.Property<int>("Status").HasColumnType("int");
            b.Property<decimal>("Total").HasPrecision(18, 2).HasColumnType("decimal(18,2)");
            b.HasKey("Id");
            b.HasIndex("CustomerId", "IdempotencyKey").IsUnique();
            b.ToTable("Orders");
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            b.Property<int>("OrderId").HasColumnType("int");
            b.Property<int>("ProductId").HasColumnType("int");
            b.Property<int>("Quantity").HasColumnType("int");
            b.Property<decimal>("UnitPrice").HasPrecision(18, 2).HasColumnType("decimal(18,2)");
            b.HasKey("Id");
            b.HasIndex("OrderId");
            b.HasIndex("ProductId");
            b.ToTable("OrderItems");
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            b.Property<DateTime>("ExpiresAt").HasColumnType("datetime2");
            b.Property<DateTime?>("RevokedAt").HasColumnType("datetime2");
            b.Property<byte[]>("RowVersion").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("nvarchar(64)");
            b.Property<int>("UserId").HasColumnType("int");
            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("UserId");
            b.ToTable("RefreshTokens");
        });

        modelBuilder.Entity<StoreOrder>().HasOne(x => x.Customer).WithMany(x => x.Orders)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade).IsRequired();
        modelBuilder.Entity<OrderItem>().HasOne(x => x.Order).WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade).IsRequired();
        modelBuilder.Entity<OrderItem>().HasOne(x => x.Product).WithMany(x => x.OrderItems)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict).IsRequired();
        modelBuilder.Entity<RefreshToken>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).IsRequired();
    }
}
