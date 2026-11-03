using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Store.Api.Domain;
using Store.Api.Infrastructure;

namespace Store.Tests;

public sealed class OrderConcurrencyTests
{
    private static readonly SemaphoreSlim DatabaseSetupLock = new(1, 1);

    [Fact]
    public async Task ParallelOrdersForLastUnit_ReturnOneCreatedAndOneConflict()
    {
        var connection = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection),
            "Set SQLSERVER_TEST_CONNECTION to a disposable SQL Server database before running integration tests.");
        await EnsureDatabaseExistsAsync(connection!);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Store", connection);
            builder.UseSetting("Jwt:Key", "integration-test-key-at-least-32-bytes-long");
            builder.UseSetting("Seed:AdminPassword", "integration-admin-password-123");
            builder.UseSetting("Seed:CustomerPassword", "integration-customer-password-123");
        });
        using var customer1 = factory.CreateClient();
        using var customer2 = factory.CreateClient();
        var email1 = $"race-{Guid.NewGuid():N}@example.com";
        var email2 = $"race-{Guid.NewGuid():N}@example.com";
        var token1 = await RegisterAsync(customer1, email1);
        var token2 = await RegisterAsync(customer2, email2);

        int productId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var product = new Product
            {
                Sku = $"RACE-{Guid.NewGuid():N}", Name = "Concurrency test item", Price = 9.99m,
                StockQuantity = 1, IsActive = true
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }

        var first = CreateOrderAsync(customer1, token1, productId, Guid.NewGuid().ToString("N"));
        var second = CreateOrderAsync(customer2, token2, productId, Guid.NewGuid().ToString("N"));
        var statuses = await Task.WhenAll(first, second);

        Assert.Single(statuses, status => status == HttpStatusCode.Created);
        Assert.Single(statuses, status => status == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RepeatedIdempotencyKey_ReturnsOriginalOrderWithoutConsumingMoreStock()
    {
        var connection = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection),
            "Set SQLSERVER_TEST_CONNECTION to a disposable SQL Server database before running integration tests.");
        await EnsureDatabaseExistsAsync(connection!);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Store", connection);
            builder.UseSetting("Jwt:Key", "integration-test-key-at-least-32-bytes-long");
            builder.UseSetting("Seed:AdminPassword", "integration-admin-password-123");
            builder.UseSetting("Seed:CustomerPassword", "integration-customer-password-123");
        });
        using var client = factory.CreateClient();
        var token = await RegisterAsync(client, $"idem-{Guid.NewGuid():N}@example.com");

        int productId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var product = new Product
            {
                Sku = $"IDEM-{Guid.NewGuid():N}", Name = "Idempotency test item", Price = 4m,
                StockQuantity = 5, IsActive = true
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }

        var key = Guid.NewGuid().ToString("N");
        var first = await CreateOrderAsync(client, token, productId, key);
        var second = await CreateOrderAsync(client, token, productId, key);
        Assert.Equal(HttpStatusCode.Created, first);
        Assert.Equal(HttpStatusCode.Created, second);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StoreDbContext>();
        Assert.Equal(4, await verifyDb.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(1, await verifyDb.Orders.CountAsync(o => o.IdempotencyKey == key));
    }

    [Fact]
    public async Task InsufficientStockForOneLine_DoesNotReserveAnyOtherLine()
    {
        var connection = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection),
            "Set SQLSERVER_TEST_CONNECTION to a disposable SQL Server database before running integration tests.");
        await EnsureDatabaseExistsAsync(connection!);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Store", connection);
            builder.UseSetting("Jwt:Key", "integration-test-key-at-least-32-bytes-long");
            builder.UseSetting("Seed:AdminPassword", "integration-admin-password-123");
            builder.UseSetting("Seed:CustomerPassword", "integration-customer-password-123");
        });
        using var client = factory.CreateClient();
        var token = await RegisterAsync(client, $"atomic-{Guid.NewGuid():N}@example.com");

        int availableProductId;
        int shortProductId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var products = new[]
            {
                new Product { Sku = $"ATOMIC-A-{Guid.NewGuid():N}", Name = "Available item", Price = 3m, StockQuantity = 4, IsActive = true },
                new Product { Sku = $"ATOMIC-B-{Guid.NewGuid():N}", Name = "Short item", Price = 5m, StockQuantity = 1, IsActive = true }
            };
            db.Products.AddRange(products);
            await db.SaveChangesAsync();
            availableProductId = products[0].Id;
            shortProductId = products[1].Id;
        }

        var key = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                items = new[]
                {
                    new { productId = availableProductId, quantity = 2 },
                    new { productId = shortProductId, quantity = 2 }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StoreDbContext>();
        Assert.Equal(4, await verifyDb.Products.Where(p => p.Id == availableProductId).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(1, await verifyDb.Products.Where(p => p.Id == shortProductId).Select(p => p.StockQuantity).SingleAsync());
        Assert.False(await verifyDb.Orders.AnyAsync(o => o.IdempotencyKey == key));
    }

    private static async Task<string> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { fullName = "Concurrency Test", email, password = "TestPassword123!" });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenDto>();
        return tokens!.AccessToken;
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        await DatabaseSetupLock.WaitAsync();
        try
        {
            var target = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(target.InitialCatalog))
                throw new InvalidOperationException("SQLSERVER_TEST_CONNECTION must name a disposable database.");
            var databaseName = target.InitialCatalog;
            target.InitialCatalog = "master";
            await using var connection = new SqlConnection(target.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF DB_ID(@databaseName) IS NULL
                BEGIN
                    DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
                    EXEC(@sql);
                END
                """;
            command.Parameters.AddWithValue("@databaseName", databaseName);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            DatabaseSetupLock.Release();
        }
    }

    private static async Task<HttpStatusCode> CreateOrderAsync(HttpClient client, string token, int productId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } } })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private sealed record TokenDto(string AccessToken, string RefreshToken, string Role);
}
