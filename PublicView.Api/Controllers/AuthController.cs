using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using PublicView.Api.Auth;
using PublicView.Api.Data;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Models;
using PublicView.Api.Services;

namespace PublicView.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableCors("spa")]
public class AuthController(AppDbContext db, CurrentUserService currentUser, IWebHostEnvironment env) : ControllerBase
{
    private static readonly Dictionary<string, string> ProviderLabels = new()
    {
        ["Google"] = "google",
        ["Facebook"] = "facebook",
        ["Microsoft"] = "microsoft",
        ["MicrosoftAccount"] = "microsoft",
        ["Instagram"] = "instagram",
        ["Twitter"] = "x",
        ["Apple"] = "apple"
    };

    [HttpGet("providers")]
    public IActionResult Providers()
    {
        var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        bool Has(string section) => !string.IsNullOrEmpty(cfg[$"Authentication:{section}:ClientId"]) ||
                                    !string.IsNullOrEmpty(cfg[$"Authentication:{section}:ConsumerKey"]);

        var providers = new List<object>();
        void Add(string id, string label, string color)
        {
            providers.Add(new { id, label, color });
        }

        if (Has("Google")) Add("Google", "Google", "#4285F4");
        if (Has("Facebook")) Add("Facebook", "Facebook", "#1877F2");
        if (Has("Microsoft")) Add("Microsoft", "Microsoft", "#00A4EF");
        if (Has("MicrosoftAccount")) Add("MicrosoftAccount", "Microsoft", "#00A4EF");
        if (Has("Instagram")) Add("Instagram", "Instagram", "#E1306C");
        if (Has("Twitter")) Add("Twitter", "X", "#111111");
        if (Has("Apple")) Add("Apple", "Apple", "#000000");

        var enabledDemo = cfg.GetValue("Auth:EnableDemo", false) || (env.IsDevelopment() && providers.Count == 0);
        if (enabledDemo) Add("demo", "Demo (skip OAuth in dev)", "#22c55e");

        return Ok(providers);
    }

    [HttpGet("login/{provider}")]
    public async Task<IActionResult> Login(string provider, [FromQuery] string? returnUrl = null)
    {
        var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var spaBase = cfg["Frontend:BaseUrl"] ?? "http://localhost:4200";

        if (provider == "demo")
        {
            var demoUser = await db.Users.FirstOrDefaultAsync(u => u.Id == Guid.Empty);
            if (demoUser is null)
            {
                demoUser = new User { Id = Guid.Empty, Email = "demo@publicview.local", DisplayName = "PublicView Demo" };
                db.Users.Add(demoUser);
                await db.SaveChangesAsync();
            }

            var identity = new ClaimsIdentity(AuthDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, demoUser.Id.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.Email, demoUser.Email));
            identity.AddClaim(new Claim(ClaimTypes.Name, demoUser.DisplayName));
            await HttpContext.SignInAsync(AuthDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Redirect($"{spaBase}{(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl)}");
        }

        var props = new AuthenticationProperties { RedirectUri = "/api/auth/complete" };
        if (!string.IsNullOrEmpty(returnUrl)) props.Items["returnUrl"] = returnUrl;
        return Challenge(props, provider);
    }

    [Authorize]
    [HttpGet("/api/auth/complete")]
    public async Task<IActionResult> Complete([FromQuery] string? returnUrl)
    {
        var user = await currentUser.GetCurrentUserAsync(HttpContext);
        if (user is null) return Redirect("/api/auth/complete?error=not_signed_in");

        var spaBase = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Frontend:BaseUrl"]
                      ?? "http://localhost:4200";
        return Redirect($"{spaBase}{(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl)}");
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await currentUser.GetCurrentUserAsync(HttpContext);
        if (user is null) return Unauthorized();
        return Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.AvatarUrl
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthDefaults.AuthenticationScheme);
        return Ok(new { ok = true });
    }
}
