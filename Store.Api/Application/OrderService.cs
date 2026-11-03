using Microsoft.EntityFrameworkCore;
using Store.Api.Domain;
using Store.Api.Infrastructure;

namespace Store.Api.Application;

public sealed class OrderService(StoreDbContext db, ProductListCache productCache, ILogger<OrderService> logger)
{
    public async Task<StoreOrder> PlaceAsync(int customerId, string idempotencyKey,
        IReadOnlyCollection<OrderLineRequest> requestedItems, CancellationToken cancellationToken)
    {
        if (!OrderRules.IsValidIdempotencyKey(idempotencyKey))
            throw new ApiException(400, "A valid Idempotency-Key header (1-128 characters) is required.");
        var quantities = OrderRules.AggregateItems(requestedItems);

        var existing = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.CustomerId == customerId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is not null) return existing;

        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            existing = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Product)
                .SingleOrDefaultAsync(x => x.CustomerId == customerId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                await tx.CommitAsync(cancellationToken);
                return existing;
            }

            var products = await db.Products.Where(p => quantities.Keys.Contains(p.Id))
                .OrderBy(p => p.Id).ToListAsync(cancellationToken);
            if (products.Count != quantities.Count)
                throw new ApiException(400, "One or more products do not exist.");
            foreach (var product in products)
            {
                if (!product.IsActive) throw new ApiException(400, $"Product '{product.Name}' is not available.");
                if (!OrderRules.HasSufficientStock(product.StockQuantity, quantities[product.Id]))
                    throw new ApiException(409, $"Insufficient stock for product '{product.Name}'.");
            }

            var subTotal = products.Sum(p => p.Price * quantities[p.Id]);
            var orderCount = await db.Orders.CountAsync(x => x.CustomerId == customerId, cancellationToken);
            var discount = OrderRules.CalculateDiscount(subTotal, orderCount);
            var order = new StoreOrder
            {
                CustomerId = customerId,
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTime.UtcNow,
                Status = OrderStatus.Pending,
                SubTotal = decimal.Round(subTotal, 2, MidpointRounding.AwayFromZero),
                Discount = discount,
                Total = decimal.Round(subTotal - discount, 2, MidpointRounding.AwayFromZero),
                Items = products.Select(p => new OrderItem
                {
                    ProductId = p.Id,
                    Quantity = quantities[p.Id],
                    UnitPrice = p.Price
                }).ToList()
            };
            foreach (var product in products) product.StockQuantity -= quantities[product.Id];
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            productCache.Invalidate();
            logger.LogInformation("Order {OrderId} placed by customer {CustomerId}", order.Id, customerId);
            return await db.Orders.Include(x => x.Items).ThenInclude(x => x.Product)
                .SingleAsync(x => x.Id == order.Id, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogInformation(ex, "Inventory concurrency conflict placing order for customer {CustomerId}", customerId);
            throw new ApiException(409, "Inventory changed while placing this order. Please review your cart and retry.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogInformation(ex, "Inventory transaction deadlock while placing order for customer {CustomerId}", customerId);
            throw new ApiException(409, "Inventory changed while placing this order. Please review your cart and retry.");
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsDeadlock(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogInformation(ex, "Inventory transaction deadlock while placing order for customer {CustomerId}", customerId);
            throw new ApiException(409, "Inventory changed while placing this order. Please review your cart and retry.");
        }
        catch (Exception ex) when (DatabaseErrors.IsDeadlock(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogInformation(ex, "Database write conflict while placing order for customer {CustomerId}", customerId);
            throw new ApiException(409, "Inventory changed while placing this order. Please review your cart and retry.");
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            existing = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Product)
                .SingleOrDefaultAsync(x => x.CustomerId == customerId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null) return existing;
            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StoreOrder> ChangeStatusAsync(int orderId, OrderStatus nextStatus,
        int? requestingCustomerId, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.Orders.Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ApiException(404, "Order not found.");
        if (requestingCustomerId.HasValue)
        {
            if (order.CustomerId != requestingCustomerId || nextStatus != OrderStatus.Cancelled)
                throw new ApiException(403, "Customers may only cancel their own orders.");
            if (!OrderRules.CanCustomerCancel(order.Status))
                throw new ApiException(400, "Customers may only cancel Pending orders.");
        }

        if (!OrderRules.CanTransition(order.Status, nextStatus))
            throw new ApiException(400, $"Cannot change order status from {order.Status} to {nextStatus}.");

        if (nextStatus == OrderStatus.Cancelled)
        {
            var products = await db.Products.Where(p => order.Items.Select(i => i.ProductId).Contains(p.Id))
                .OrderBy(p => p.Id).ToListAsync(cancellationToken);
            foreach (var item in order.Items)
            {
                var product = products.Single(p => p.Id == item.ProductId);
                product.StockQuantity = checked(product.StockQuantity + item.Quantity);
            }
        }
        order.Status = nextStatus;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            if (nextStatus == OrderStatus.Cancelled) productCache.Invalidate();
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(cancellationToken);
            throw new ApiException(409, "Inventory changed while updating the order. Please retry.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205)
        {
            await tx.RollbackAsync(cancellationToken);
            throw new ApiException(409, "Inventory changed while updating the order. Please retry.");
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsDeadlock(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            throw new ApiException(409, "Inventory changed while updating the order. Please retry.");
        }
        catch (Exception ex) when (DatabaseErrors.IsDeadlock(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            throw new ApiException(409, "Inventory changed while updating the order. Please retry.");
        }
        return order;
    }

    public async Task<int> CancelExpiredAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        var ids = await db.Orders.Where(o => o.Status == OrderStatus.Pending && o.CreatedAt < cutoff)
            .OrderBy(o => o.Id).Select(o => o.Id).Take(100).ToListAsync(cancellationToken);
        var cancelled = 0;
        foreach (var id in ids)
        {
            try
            {
                await ChangeStatusAsync(id, OrderStatus.Cancelled, null, cancellationToken);
                cancelled++;
            }
            catch (ApiException ex) when (ex.StatusCode is 400 or 404 or 409)
            {
                logger.LogInformation(ex, "Expired order {OrderId} was not cancelled due to a concurrent update", id);
            }
        }
        return cancelled;
    }

}
