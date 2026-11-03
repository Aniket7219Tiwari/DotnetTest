using Microsoft.EntityFrameworkCore;
using Store.Api.Domain;
using Store.Api.Infrastructure;

namespace Store.Api.Application;

public static class ReportsQueries
{
    public static IQueryable<TopCustomerDto> TopCustomers(StoreDbContext db, DateTime since)
    {
        var spending = db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= since &&
                        (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped))
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, TotalSpend = g.Sum(o => o.Total) });

        return spending.Join(db.Users.AsNoTracking(), x => x.CustomerId, user => user.Id,
                (spend, user) => new { Spend = spend, Customer = user })
            .OrderByDescending(x => x.Spend.TotalSpend).ThenBy(x => x.Customer.Id).Take(5)
            .Select(x => new TopCustomerDto(x.Spend.CustomerId, x.Customer.FullName,
                x.Customer.Email, x.Spend.TotalSpend));
    }
}
