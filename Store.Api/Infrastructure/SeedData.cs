using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Store.Api.Domain;

namespace Store.Api.Infrastructure;

public static class SeedData
{
    private static readonly (string Name, decimal Price)[] Products =
    [
        ("Stainless Steel Water Bottle", 799m),
        ("Wireless Bluetooth Earbuds", 2499m),
        ("Compact USB-C Fast Charger", 1299m),
        ("Classic Ceramic Coffee Mug", 399m),
        ("Canvas Everyday Tote Bag", 699m),
        ("Portable Bluetooth Speaker", 2199m),
        ("Adjustable Aluminum Laptop Stand", 1599m),
        ("Soft Cotton Bath Towel", 899m),
        ("Insulated Travel Coffee Tumbler", 999m),
        ("LED Desk Lamp with USB Port", 1799m),
        ("Ergonomic Wireless Mouse", 1199m),
        ("A5 Hardcover Notebook Set", 549m),
        ("Fast-Charging USB-C Cable", 399m),
        ("Lightweight Exercise Yoga Mat", 1499m),
        ("Compact Digital Kitchen Scale", 899m)
    ];

    public static async Task InitializeAsync(IServiceProvider services, bool development = false,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        if (db.Database.IsSqlite())
            await db.Database.EnsureCreatedAsync(cancellationToken);
        else
            await db.Database.MigrateAsync(cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken))
        {
            if (development)
                await UpdatePlaceholderProductsAsync(db, cancellationToken);
            return;
        }
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");
        var adminPassword = configuration["Seed:AdminPassword"];
        var customerPassword = configuration["Seed:CustomerPassword"];
        if (development)
        {
            adminPassword ??= Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            customerPassword ??= Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            logger.LogWarning("Local development seed credentials: admin@example.com / {AdminPassword}; customer1@example.com / {CustomerPassword}",
                adminPassword, customerPassword);
        }
        else if (string.IsNullOrWhiteSpace(adminPassword) || string.IsNullOrWhiteSpace(customerPassword))
        {
            throw new InvalidOperationException("Set Seed:AdminPassword and Seed:CustomerPassword before seeding an empty database.");
        }
        if (adminPassword.Length < 12 || customerPassword.Length < 12)
            throw new InvalidOperationException("Seed passwords must contain at least 12 characters.");
        var hasher = new PasswordHasher<User>();
        var users = new[]
        {
            new User { FullName = "Store Admin", Email = "admin@example.com", Role = UserRole.Admin },
            new User { FullName = "Aarav Sharma", Email = "customer1@example.com" },
            new User { FullName = "Diya Patel", Email = "customer2@example.com" },
            new User { FullName = "Kabir Singh", Email = "customer3@example.com" }
        };
        foreach (var user in users)
            user.PasswordHash = hasher.HashPassword(user, user.Role == UserRole.Admin ? adminPassword : customerPassword);
        db.Users.AddRange(users);
        db.Products.AddRange(Products.Select((product, index) => new Product
        {
            Sku = $"SKU-{index + 1:000}",
            Name = product.Name,
            Price = product.Price,
            StockQuantity = 10 + index + 1,
            IsActive = true
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpdatePlaceholderProductsAsync(
        StoreDbContext db, CancellationToken cancellationToken)
    {
        var products = await db.Products
            .Where(product => product.Name.StartsWith("Store Product ") &&
                              product.Sku.StartsWith("SKU-"))
            .ToListAsync(cancellationToken);
        foreach (var product in products)
        {
            if (!int.TryParse(product.Sku.AsSpan("SKU-".Length), out var number) ||
                number < 1 || number > Products.Length)
                continue;

            var catalogEntry = Products[number - 1];
            product.Name = catalogEntry.Name;
            product.Price = catalogEntry.Price;
        }

        if (products.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }
}
