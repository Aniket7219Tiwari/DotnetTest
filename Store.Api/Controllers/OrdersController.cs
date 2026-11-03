using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Store.Api.Application;
using Store.Api.Domain;
using Store.Api.Infrastructure;

namespace Store.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public sealed class OrdersController(StoreDbContext db, OrderService orders) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var order = await orders.PlaceAsync(UserId, Request.Headers["Idempotency-Key"].ToString(), request.Items, ct);
        var dto = await ToDto(order.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, dto);
    }

    [HttpGet("my")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<PageResponse<OrderDto>>> MyOrders([FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest("Invalid pagination.");
        var q = db.Orders.AsNoTracking().Where(o => o.CustomerId == UserId);
        var count = await q.CountAsync(ct);
        var result = await q.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderDto(o.Id, o.CustomerId, o.Status.ToString(), o.SubTotal, o.Discount,
                o.Total, o.CreatedAt, o.Items.Select(i => new OrderItemDto(i.ProductId, i.Product.Name,
                    i.Quantity, i.UnitPrice)).ToList()))
            .ToListAsync(ct);
        return Ok(new PageResponse<OrderDto>(result, page, pageSize, count));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PageResponse<OrderDto>>> All([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest("Invalid pagination.");
        var q = db.Orders.AsNoTracking();
        var count = await q.CountAsync(ct);
        var result = await q.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderDto(o.Id, o.CustomerId, o.Status.ToString(), o.SubTotal, o.Discount,
                o.Total, o.CreatedAt, o.Items.Select(i => new OrderItemDto(i.ProductId, i.Product.Name,
                    i.Quantity, i.UnitPrice)).ToList()))
            .ToListAsync(ct);
        return Ok(new PageResponse<OrderDto>(result, page, pageSize, count));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> Get(int id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new OrderDto(o.Id, o.CustomerId, o.Status.ToString(), o.SubTotal, o.Discount,
                o.Total, o.CreatedAt, o.Items.Select(i => new OrderItemDto(i.ProductId, i.Product.Name,
                    i.Quantity, i.UnitPrice)).ToList()))
            .SingleOrDefaultAsync(ct);
        if (order is null) return NotFound();
        if (!User.IsInRole("Admin") && order.CustomerId != UserId) return Forbid();
        return Ok(order);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<OrderDto>> ChangeStatus(int id, ChangeStatusRequest request, CancellationToken ct)
    {
        int? customerId = null;
        if (User.IsInRole("Admin"))
        {
            if (!Enum.IsDefined(request.Status)) return BadRequest("Unknown order status.");
        }
        else
        {
            if (!User.IsInRole("Customer")) return Forbid();
            customerId = UserId;
        }
        await orders.ChangeStatusAsync(id, request.Status, customerId, ct);
        return Ok(await ToDto(id, ct));
    }

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<OrderDto> ToDto(int id, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new OrderDto(o.Id, o.CustomerId, o.Status.ToString(), o.SubTotal, o.Discount,
                o.Total, o.CreatedAt, o.Items.Select(i => new OrderItemDto(i.ProductId, i.Product.Name,
                    i.Quantity, i.UnitPrice)).ToList())).SingleAsync(ct);
}
