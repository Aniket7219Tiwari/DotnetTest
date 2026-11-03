using Store.Api.Application;
using Store.Api.Domain;

namespace Store.Tests;

public sealed class OrderRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void IdempotencyKey_RejectsMissingOrOversizedValues(string? key) =>
        Assert.False(OrderRules.IsValidIdempotencyKey(key));

    [Fact]
    public void IdempotencyKey_AcceptsNonEmptyKeyWithinLimit() =>
        Assert.True(OrderRules.IsValidIdempotencyKey("checkout-01"));

    [Fact]
    public void AggregateItems_CombinesDuplicateProductLines()
    {
        var result = OrderRules.AggregateItems([new(1, 2), new(1, 3), new(2, 1)]);
        Assert.Equal(5, result[1]);
        Assert.Equal(1, result[2]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    public void AggregateItems_RejectsInvalidProductOrQuantity(int productId, int quantity) =>
        Assert.Throws<ApiException>(() => OrderRules.AggregateItems([new(productId, quantity)]));

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(4, 4, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 2, false)]
    public void StockValidation_DetectsWhetherAllRequestedUnitsAreAvailable(int stock, int requested, bool expected) =>
        Assert.Equal(expected, OrderRules.HasSufficientStock(stock, requested));

    [Theory]
    [InlineData(10000, 0, 0)]
    [InlineData(10000.01, 0, 500.00)]
    [InlineData(1000, 4, 100.00)]
    [InlineData(10000.01, 4, 1500.00)]
    [InlineData(1000.05, 4, 100.01)]
    public void Discount_UsesThresholdFifthOrderAndTwoDecimalRounding(decimal subtotal, int priorOrders, decimal expected) =>
        Assert.Equal(expected, OrderRules.CalculateDiscount(subtotal, priorOrders));

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Confirmed, false)]
    public void StatusTransitions_EnforceAllowedWorkflow(OrderStatus current, OrderStatus next, bool expected) =>
        Assert.Equal(expected, OrderRules.CanTransition(current, next));

    [Theory]
    [InlineData(OrderStatus.Pending, true)]
    [InlineData(OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void CustomerCancellation_IsAllowedOnlyWhilePending(OrderStatus status, bool expected) =>
        Assert.Equal(expected, OrderRules.CanCustomerCancel(status));
}
