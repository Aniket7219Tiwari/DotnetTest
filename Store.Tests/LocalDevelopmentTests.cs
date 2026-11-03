using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Store.Api.Application;
using Store.Api.Infrastructure;

namespace Store.Tests;

public sealed class LocalDevelopmentTests
{
    [Fact]
    public async Task SqliteDevelopmentModeSupportsRegistrationLoginCheckoutReplayAndCancellation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"store-local-{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
                builder.UseSetting("Database:Provider", "Sqlite");
                builder.UseSetting("ConnectionStrings:Store", $"Data Source={databasePath};Default Timeout=30");
                builder.UseSetting("Jwt:Key", "sqlite-local-integration-key-not-production");
                builder.UseSetting("Seed:AdminPassword", "integration-admin-password-123");
                builder.UseSetting("Seed:CustomerPassword", "integration-customer-password-123");
            });
            using var client = factory.CreateClient();

            var email = $"local-{Guid.NewGuid():N}@example.com";
            const string password = "customer-password-123";
            using var registration = await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("Local Test Customer", email, password));
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

            using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
            Assert.NotNull(tokens);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var products = await client.GetFromJsonAsync<PageResponse<ProductDto>>("/api/products?page=1&pageSize=15");
            Assert.NotNull(products);
            Assert.Equal(15, products.TotalCount);
            var product = products.Items.First();
            var initialStock = product.StockQuantity;

            var idempotencyKey = Guid.NewGuid().ToString("N");
            using var firstOrderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new PlaceOrderRequest([new OrderLineRequest(product.Id, 1)]))
            };
            firstOrderRequest.Headers.Add("Idempotency-Key", idempotencyKey);
            using var firstOrderResponse = await client.SendAsync(firstOrderRequest);
            Assert.Equal(HttpStatusCode.Created, firstOrderResponse.StatusCode);
            var originalOrder = await firstOrderResponse.Content.ReadFromJsonAsync<OrderDto>();
            Assert.NotNull(originalOrder);

            using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new PlaceOrderRequest([new OrderLineRequest(product.Id, 1)]))
            };
            replayRequest.Headers.Add("Idempotency-Key", idempotencyKey);
            using var replayResponse = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
            var replayedOrder = await replayResponse.Content.ReadFromJsonAsync<OrderDto>();
            Assert.Equal(originalOrder.Id, replayedOrder?.Id);

            using var reservedScope = factory.Services.CreateScope();
            var db = reservedScope.ServiceProvider.GetRequiredService<StoreDbContext>();
            Assert.Equal(initialStock - 1,
                await db.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
            Assert.Equal(1, await db.Orders.CountAsync(o => o.IdempotencyKey == idempotencyKey));

            using var cancelResponse = await client.PatchAsJsonAsync(
                $"/api/orders/{originalOrder.Id}/status", new ChangeStatusRequest(Store.Api.Domain.OrderStatus.Cancelled));
            Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StoreDbContext>();
            Assert.Equal(initialStock,
                await verifyDb.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var file = databasePath + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
