using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Store.Api.Application;
using Store.Api.Infrastructure;

namespace Store.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/reports")]
public sealed class ReportsController(StoreDbContext db) : ControllerBase
{
    [HttpGet("top-customers")]
    public async Task<ActionResult<IReadOnlyCollection<TopCustomerDto>>> TopCustomers(
        [FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (days is < 1 or > 3650) return BadRequest("Days must be between 1 and 3650.");
        var since = DateTime.UtcNow.AddDays(-days);
        var result = await ReportsQueries.TopCustomers(db, since).ToListAsync(ct);
        return Ok(result);
    }
}
