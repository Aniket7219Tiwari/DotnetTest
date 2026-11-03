namespace Store.Api.Domain;

public enum UserRole { Admin, Customer }
public enum OrderStatus { Pending, Confirmed, Shipped, Cancelled }

public sealed class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Customer;
    public ICollection<StoreOrder> Orders { get; set; } = new List<StoreOrder>();
}

public sealed class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

public sealed class StoreOrder
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public User Customer { get; set; } = null!;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public StoreOrder Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
