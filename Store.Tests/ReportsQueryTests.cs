using Microsoft.EntityFrameworkCore;
using Store.Api.Application;
using Store.Api.Infrastructure;
using Xunit.Abstractions;

namespace Store.Tests;

public sealed class ReportsQueryTests(ITestOutputHelper output)
{
    [Fact]
    public void TopCustomers_IsOneServerSideGroupedQuery()
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlServer("Server=localhost;Database=Store;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var db = new StoreDbContext(options);
        var sql = ReportsQueries.TopCustomers(db, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .ToQueryString();

        output.WriteLine(sql);
        Assert.Contains("SELECT TOP(", sql);
        Assert.Contains("int = 5", sql);
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("SUM(", sql);
        Assert.Contains("INNER JOIN [Users]", sql);
        Assert.Contains("[Status] IN (1, 2)", sql);
    }
}
