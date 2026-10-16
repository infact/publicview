using System.Text.Json;

namespace PublicView.Api.Services;

public record AiSearchResult(string? Keywords, string? CategorySlug, decimal? MinPrice, decimal? MaxPrice, string? City, string? AiSummary);

public record AiListingResult(string? PolishedTitle, string? PolishedDescription, decimal? SuggestedPrice, string? SuggestedCategorySlug, string? Tip);

public interface IAiService
{
    Task<AiSearchResult> InterpretQueryAsync(string query);
    Task<AiListingResult> EnhanceListingAsync(string title, string description, string? categorySlug, decimal? price);
    Task<string> GenerateKeywordsAsStringAsync(string title, string description);
}

public class AiService(IConfiguration config, ILogger<AiService> logger) : IAiService
{
    public bool IsConfigured => !string.IsNullOrEmpty(config["Ai:ApiKey"]);

    public async Task<AiSearchResult> InterpretQueryAsync(string query)
    {
        if (!IsConfigured) return HeuristicSearch(query);

        try
        {
            var client = CreateClient();
            var system =
                """
                You interpret Craigslist-style classifieds searches. The user gives a loose search phrase.
                Respond ONLY with compact JSON: {"keywords":"lowercase space separated search terms",
                "category": one of [jobs,housing,for-sale,services,gigs,resumes,community,personals-ads,events,lost-found] or null,
                "minPrice": number or null, "maxPrice": number or null, "city": string or null, "summary": "short phrase explaining what user is looking for"}
                """;
            var completion = await client.CompleteChatAsync(
                [system, $"User search: {query}"]);
            var json = ExtractJson(completion.Value.Content[0].Text ?? string.Empty);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new AiSearchResult(
                root.TryGetProperty("keywords", out var k) ? k.GetString() : null,
                root.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
                root.TryGetProperty("minPrice", out var min) && min.ValueKind == JsonValueKind.Number && min.TryGetDecimal(out var mv) ? mv : null,
                root.TryGetProperty("maxPrice", out var max) && max.ValueKind == JsonValueKind.Number && max.TryGetDecimal(out var xv) ? xv : null,
                root.TryGetProperty("city", out var city) && city.ValueKind == JsonValueKind.String ? city.GetString() : null,
                root.TryGetProperty("summary", out var s) ? s.GetString() : null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI query interpretation failed, falling back to heuristic search");
            return HeuristicSearch(query);
        }
    }

    public async Task<AiListingResult> EnhanceListingAsync(string title, string description, string? categorySlug, decimal? price)
    {
        if (!IsConfigured) return new AiListingResult(title, description, price, categorySlug, null);

        try
        {
            var client = CreateClient();
            var system =
                """
                You help people post great classified ads. Given a rough draft, return ONLY compact JSON:
                {"title": "a catchy but honest title, max 60 chars", "description": "a polished, friendly 3-6 sentence description with concrete details",
                 "suggestedPrice": number (the asking amount in USD, null if not applicable), "category": one of [jobs,housing,for-sale,services,gigs,resumes,community,personals-ads,events,lost-found],
                 "tip": "one short tip to get the ad noticed (max 12 words)"}
                """;
            var completion = await client.CompleteChatAsync(
                [system, $"Draft:\nTitle: {title}\nDescription: {description}\nStated category: {categorySlug ?? "unknown"}\nStated price: {price?.ToString() ?? "none"}"]);
            var json = ExtractJson(completion.Value.Content[0].Text ?? string.Empty);
            var root = JsonDocument.Parse(json).RootElement;
            return new AiListingResult(
                root.TryGetProperty("title", out var t) ? t.GetString() : title,
                root.TryGetProperty("description", out var d) ? d.GetString() : description,
                root.TryGetProperty("suggestedPrice", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var pv) ? pv : price,
                root.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : categorySlug,
                root.TryGetProperty("tip", out var tip) ? tip.GetString() : null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI listing enhancement failed");
            return new AiListingResult(title, description, price, categorySlug, null);
        }
    }

    public async Task<string> GenerateKeywordsAsStringAsync(string title, string description)
    {
        var heuristics = string.Join(' ',
            (title + " " + description)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Distinct()
            .Take(25));
        if (!IsConfigured) return heuristics;

        try
        {
            var client = CreateClient();
            var completion = await client.CompleteChatAsync(
                ["You generate lowercase search keywords for a classifieds post. Reply with lowercase words separated by single spaces. Max 20 words. No punctuation.",
                 $"Title: {title}\nDescription: {description}"]);
            var keywords = (completion.Value.Content[0].Text ?? "").Trim();
            return string.IsNullOrWhiteSpace(keywords) ? heuristics : keywords;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI keyword generation failed, using heuristic keywords");
            return heuristics;
        }
    }

    private OpenAI.Chat.ChatClient CreateClient()
    {
        var model = config["Ai:Model"] ?? "gpt-4o-mini";
        return new OpenAI.Chat.ChatClient(model, config["Ai:ApiKey"]);
    }

    private static AiSearchResult HeuristicSearch(string query)
    {
        var q = query.ToLowerInvariant();
        string? category = null;
        decimal? min = null, max = null;

        if (q.Contains("job") || q.Contains("hire") || q.Contains("hiring") || q.Contains("position")) category = "jobs";
        else if (q.Contains("apartment") || q.Contains("rent") || q.Contains("room") || q.Contains("house for")) category = "housing";
        else if (q.Contains("buy") || q.Contains("used") || q.Contains("sale") || q.Contains("cheap") || q.Contains("free")) category = "for-sale";
        else if (q.Contains("repair") || q.Contains("plumber") || q.Contains("electrician") || q.Contains("cleaning") || q.Contains("moving")) category = "services";

        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (decimal.TryParse(word.TrimStart('$'), out var n) && n < 1_000_000)
            {
                if (q.Contains("under") || q.Contains("below") || q.Contains("<")) max = n;
                else if (q.Contains("over") || q.Contains("above") || q.Contains(">")) min = n;
                else if (max is null && min is null) max = n;
            }
        }

        return new AiSearchResult(null, category, min, max, null, null);
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }
}
