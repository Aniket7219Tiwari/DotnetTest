using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Store.Api.Infrastructure;

public sealed class StoreDbContextFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    public StoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Store")
            ?? "Server=localhost;Database=StoreDb;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new StoreDbContext(options);
    }
}
