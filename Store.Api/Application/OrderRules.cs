using Store.Api.Domain;

namespace Store.Api.Application;

public static class OrderRules
{
    public static bool IsValidIdempotencyKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= 128;

    public static Dictionary<int, int> AggregateItems(IReadOnlyCollection<OrderLineRequest> items)
    {
        if (items.Count == 0 || items.Any(x => x.ProductId <= 0 || x.Quantity <= 0))
            throw new ApiException(400, "At least one item with a positive product ID and quantity is required.");
        try { return items.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => checked(g.Sum(x => x.Quantity))); }
        catch (OverflowException) { throw new ApiException(400, "Requested quantity is too large."); }
    }

    public static decimal CalculateDiscount(decimal subTotal, int priorOrderCount)
    {
        var discountRate = (subTotal > 10_000m ? 0.05m : 0m) + ((priorOrderCount + 1) % 5 == 0 ? 0.10m : 0m);
        return decimal.Round(subTotal * discountRate, 2, MidpointRounding.AwayFromZero);
    }

    public static bool HasSufficientStock(int available, int requested) =>
        available >= 0 && requested > 0 && available >= requested;

    public static bool CanCustomerCancel(OrderStatus status) => status == OrderStatus.Pending;

    public static bool CanTransition(OrderStatus current, OrderStatus next) =>
        (current, next) switch
        {
            (OrderStatus.Pending, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
            (OrderStatus.Pending or OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
            _ => false
        };
}
