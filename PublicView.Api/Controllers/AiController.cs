using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using PublicView.Api.Services;

namespace PublicView.Api.Controllers;

public record InterpretSearchRequest(string Query);
public record EnhanceListingRequest(string Title, string Description, string? CategorySlug, decimal? Price);

[ApiController]
[Route("api/ai")]
[EnableCors("spa")]
public class AiController(IAiService ai) : ControllerBase
{
    [HttpPost("interpret-search")]
    public async Task<IActionResult> InterpretSearch([FromBody] InterpretSearchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Query)) return BadRequest();
        var result = await ai.InterpretQueryAsync(req.Query);
        return Ok(new
        {
            keywords = result.Keywords,
            category = result.CategorySlug,
            minPrice = result.MinPrice,
            maxPrice = result.MaxPrice,
            city = result.City,
            summary = result.AiSummary
        });
    }

    [HttpPost("enhance-listing")]
    public async Task<IActionResult> EnhanceListing([FromBody] EnhanceListingRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest();
        var result = await ai.EnhanceListingAsync(req.Title, req.Description, req.CategorySlug, req.Price);
        return Ok(new
        {
            title = result.PolishedTitle,
            description = result.PolishedDescription,
            suggestedPrice = result.SuggestedPrice,
            suggestedCategory = result.SuggestedCategorySlug,
            tip = result.Tip
        });
    }
}
