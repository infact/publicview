using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using PublicView.Api.Data;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Models;
using PublicView.Api.Services;

namespace PublicView.Api.Controllers;

public record CreatePostingRequest(
    Guid CategoryId,
    string Title,
    string Description,
    decimal? Price,
    string? City,
    string? ContactInfo,
    string Condition,
    List<string>? ImageUrls);

public record SearchRequest(
    string? Q,
    string? Category,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? City,
    string? Sort,
    int Page,
    int PageSize,
    bool UseAi);

[ApiController]
[Route("api/postings")]
[EnableCors("spa")]
public class PostingsController(AppDbContext db, CurrentUserService currentUser, IAiService aiService) : ControllerBase
{
    public class SearchResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal? Price { get; set; }
        public string? City { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; }
        public string CategorySlug { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }

    private IQueryable<Posting> PublishedPostings() =>
        db.Postings
            .Include(p => p.Category)
            .Where(p => p.Status == PostingStatus.Published && p.ExpiresAt > DateTimeOffset.UtcNow && p.UserId != Guid.Empty);

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] string? city,
        [FromQuery] string? sort,
        [FromQuery] bool ai = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        pageSize = Math.Clamp(pageSize, 1, 48);
        string? keywords = q;
        string? aiCategory = null;
        string? aiSummary = null;

        if (ai && !string.IsNullOrWhiteSpace(q))
        {
            var interpretation = await aiService.InterpretQueryAsync(q);
            if (!string.IsNullOrEmpty(interpretation.Keywords)) keywords = interpretation.Keywords;
            aiCategory = interpretation.CategorySlug;
            aiSummary = interpretation.AiSummary;
            minPrice ??= interpretation.MinPrice;
            maxPrice ??= interpretation.MaxPrice;
            category ??= interpretation.CategorySlug;
            city ??= interpretation.City;
        }

        var query = PublishedPostings();

        if (!string.IsNullOrWhiteSpace(keywords))
        {
            foreach (var term in keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))
            {
                query = query.Where(p =>
                    EF.Functions.ILike(p.Title, $"%{term}%") ||
                    EF.Functions.ILike(p.Description, $"%{term}%") ||
                    p.AiKeywords != null && EF.Functions.ILike(p.AiKeywords, $"%{term}%"));
            }
        }

        if (!string.IsNullOrEmpty(category)) query = query.Where(p => p.Category.Slug == category);
        if (minPrice is not null) query = query.Where(p => p.Price >= minPrice);
        if (maxPrice is not null) query = query.Where(p => p.Price <= maxPrice);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(p => p.City != null && EF.Functions.ILike(p.City, $"%{city}%"));

        query = sort switch
        {
            "price-asc" => query.OrderBy(p => p.Price),
            "price-desc" => query.OrderByDescending(p => p.Price),
            _ => query.OrderByDescending(p => p.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new SearchResponse
            {
                Id = p.Id,
                Title = p.Title,
                Description = p.Description,
                Price = p.Price,
                City = p.City,
                ImageUrls = p.ImageUrls,
                CreatedAt = p.CreatedAt,
                CategorySlug = p.Category.Slug,
                CategoryName = p.Category.Name
            })
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            aiCategoryFound = !string.IsNullOrEmpty(aiCategory),
            aiKeywordsUsed = keywords != q,
            aiSummary,
            items
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var posting = await PublishedPostings().FirstOrDefaultAsync(p => p.Id == id);
        if (posting is null) return NotFound();

        var owner = await db.Users.FindAsync(posting.UserId);
        return Ok(new
        {
            posting.Id,
            posting.Title,
            posting.Description,
            posting.Price,
            posting.City,
            posting.Condition,
            posting.ImageUrls,
            posting.CreatedAt,
            posting.CategoryId,
            CategorySlug = posting.Category.Slug,
            CategoryName = posting.Category.Name,
            posting.ContactInfo,
            Owner = new { owner!.DisplayName, Avatar = owner.AvatarUrl }
        });
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var user = await currentUser.GetCurrentUserAsync(HttpContext);
        if (user is null) return Unauthorized();

        var items = await db.Postings
            .Include(p => p.Category)
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Price,
                p.Status,
                p.CreatedAt,
                p.ExpiresAt,
                CategoryName = p.Category.Name
            })
            .ToListAsync();

        return Ok(items);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePostingRequest req)
    {
        var user = await currentUser.GetCurrentUserAsync(HttpContext);
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Description))
            return BadRequest(new { error = "Title and description are required." });

        var category = await db.Categories.FindAsync(req.CategoryId);
        if (category is null) return BadRequest(new { error = "Unknown category." });

        var keywords = await aiService.GenerateKeywordsAsStringAsync(req.Title, req.Description);
        var posting = new Posting
        {
            Id = Guid.NewGuid(),
            Title = req.Title.Trim(),
            Description = req.Description.Trim(),
            Price = req.Price,
            City = req.City?.Trim(),
            ContactInfo = req.ContactInfo,
            Condition = req.Condition,
            ImageUrls = req.ImageUrls ?? new(),
            AiKeywords = keywords,
            CategoryId = req.CategoryId,
            UserId = user.Id,
            Status = PostingStatus.PendingPayment,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };

        db.Postings.Add(posting);
        await db.SaveChangesAsync();

        return Ok(new { posting.Id, posting.Status });
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await currentUser.GetCurrentUserAsync(HttpContext);
        if (user is null) return Unauthorized();

        var posting = await db.Postings.FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);
        if (posting is null) return NotFound();

        posting.Status = PostingStatus.Removed;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
