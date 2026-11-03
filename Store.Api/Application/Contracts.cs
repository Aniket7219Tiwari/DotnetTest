using Store.Api.Domain;

namespace Store.Api.Application;

public sealed record RegisterRequest(string FullName, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, string Role);
public sealed record ProductRequest(string Sku, string Name, decimal Price, int StockQuantity, bool IsActive = true);
public sealed record ProductDto(int Id, string Sku, string Name, decimal Price, int StockQuantity, bool IsActive);
public sealed record OrderLineRequest(int ProductId, int Quantity);
public sealed record PlaceOrderRequest(IReadOnlyCollection<OrderLineRequest> Items);
public sealed record OrderItemDto(int ProductId, string ProductName, int Quantity, decimal UnitPrice);
public sealed record OrderDto(int Id, int CustomerId, string Status, decimal SubTotal, decimal Discount,
    decimal Total, DateTime CreatedAt, IReadOnlyCollection<OrderItemDto> Items);
public sealed record ChangeStatusRequest(OrderStatus Status);
public sealed record PageResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount);
public sealed record TopCustomerDto(int CustomerId, string FullName, string Email, decimal TotalSpend);
public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
