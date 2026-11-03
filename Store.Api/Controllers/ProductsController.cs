using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Store.Api.Application;
using Store.Api.Domain;
using Store.Api.Infrastructure;

namespace Store.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(StoreDbContext db, ProductListCache cache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PageResponse<ProductDto>>> List([FromQuery] string? name,
        [FromQuery] decimal? minPrice, [FromQuery] decimal? maxPrice, [FromQuery] bool? inStock,
        [FromQuery] string? sort = "name", [FromQuery] string? direction = "asc",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12, CancellationToken ct = default)
    {
        if (minPrice < 0 || maxPrice < 0 || (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice))
            return BadRequest(new ProblemDetails { Status = 400, Detail = "Price range is invalid." });
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new ProblemDetails { Status = 400, Detail = "Page must be positive and pageSize must be 1-100." });
        var key = $"products:{name?.Trim().ToLowerInvariant()}:{minPrice}:{maxPrice}:{inStock}:{sort}:{direction}:{page}:{pageSize}";
        var generation = cache.Generation;
        if (cache.TryGet<PageResponse<ProductDto>>(key, generation, out var cached) && cached is not null) return Ok(cached);

        IQueryable<Product> query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(p => p.Name.Contains(name.Trim()));
        if (minPrice.HasValue) query = query.Where(p => p.Price >= minPrice);
        if (maxPrice.HasValue) query = query.Where(p => p.Price <= maxPrice);
        if (inStock == true) query = query.Where(p => p.StockQuantity > 0);
        query = (sort?.ToLowerInvariant(), direction?.ToLowerInvariant()) switch
        {
            ("price", "desc") => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            ("price", _) => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            ("name", "desc") => query.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
            ("stock", "desc") => query.OrderByDescending(p => p.StockQuantity).ThenBy(p => p.Id),
            ("stock", _) => query.OrderBy(p => p.StockQuantity).ThenBy(p => p.Id),
            (_, "desc") => query.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderBy(p => p.Name).ThenBy(p => p.Id)
        };
        var count = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Price, p.StockQuantity, p.IsActive))
            .ToListAsync(ct);
        var result = new PageResponse<ProductDto>(items, page, pageSize, count);
        cache.Set(key, result, generation);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> Get(int id, CancellationToken ct)
    {
        var item = await db.Products.AsNoTracking().Where(p => p.Id == id && p.IsActive)
            .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Price, p.StockQuantity, p.IsActive))
            .SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(ProductRequest request, CancellationToken ct)
    {
        if (!Valid(request)) return BadRequest(new ProblemDetails { Status = 400, Detail = "SKU/name, non-negative price and stock are required." });
        var product = new Product { Sku = request.Sku.Trim(), Name = request.Name.Trim(),
            Price = request.Price, StockQuantity = request.StockQuantity, IsActive = request.IsActive };
        db.Products.Add(product);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        { return Conflict(new ProblemDetails { Status = 409, Detail = "SKU must be unique." }); }
        cache.Invalidate();
        return CreatedAtAction(nameof(Get), new { id = product.Id }, ToDto(product));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductDto>> Update(int id, ProductRequest request, CancellationToken ct)
    {
        if (!Valid(request)) return BadRequest(new ProblemDetails { Status = 400, Detail = "SKU/name, non-negative price and stock are required." });
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        product.Sku = request.Sku.Trim(); product.Name = request.Name.Trim();
        product.Price = request.Price; product.StockQuantity = request.StockQuantity; product.IsActive = request.IsActive;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new ProblemDetails { Status = 409, Detail = "Product inventory changed; reload and retry." }); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        { return Conflict(new ProblemDetails { Status = 409, Detail = "SKU must be unique." }); }
        cache.Invalidate();
        return Ok(ToDto(product));
    }

    private static bool Valid(ProductRequest x) => !string.IsNullOrWhiteSpace(x.Sku) &&
        x.Sku.Length <= 64 && !string.IsNullOrWhiteSpace(x.Name) && x.Name.Length <= 200 &&
        x.Price >= 0 && x.StockQuantity >= 0;
    private static ProductDto ToDto(Product p) => new(p.Id, p.Sku, p.Name, p.Price, p.StockQuantity, p.IsActive);
}
