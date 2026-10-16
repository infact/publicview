using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Data;

namespace PublicView.Api.Controllers;

[ApiController]
[Route("api/categories")]
[EnableCors("spa")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var categories = await db.Categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.Slug, c.Name, c.Icon })
            .ToListAsync();
        return Ok(categories);
    }
}
