using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Data;
using PublicView.Api.Models;

namespace PublicView.Api.Auth;

public sealed class SocialAuthEvents(AppDbContext db, ILogger<SocialAuthEvents> logger)
{
    public Task<bool> OnTicketReceivedAsync(TicketReceivedContext context) =>
        OnTicketReceivedInternal(context);

    private async Task<bool> OnTicketReceivedInternal(TicketReceivedContext context)
    {
        var provider = context.Scheme.Name;
        var externalUser = context.Principal;
        var providerKey = externalUser?.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = externalUser?.FindFirstValue(ClaimTypes.Email) ?? externalUser?.FindFirstValue("preferred_username");
        var displayName = externalUser?.FindFirstValue(ClaimTypes.Name) ?? email ?? providerKey ?? "Member";
        var avatar = externalUser?.FindFirstValue("picture") ?? externalUser?.FindFirstValue(ClaimTypes.Uri);

        if (string.IsNullOrEmpty(providerKey))
        {
            logger.LogWarning("Ticket received from {Provider} without a stable identifier", provider);
            context.Response.Redirect("/api/auth/complete?error=no_identifier");
            return true;
        }

        var login = await db.ExternalLogins
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Provider == provider && l.ProviderKey == providerKey);

        var user = login?.User;
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email ?? string.Empty,
                DisplayName = displayName,
                AvatarUrl = avatar
            };
            user.Logins.Add(new ExternalLogin
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                ProviderKey = providerKey,
                Email = email,
                UserId = user.Id
            });
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(email) && user.Email != email) user.Email = email;
            if (!string.IsNullOrWhiteSpace(displayName) && user.DisplayName != displayName) user.DisplayName = displayName;
            if (!string.IsNullOrWhiteSpace(avatar) && user.AvatarUrl != avatar) user.AvatarUrl = avatar;
            await db.SaveChangesAsync();
        }

        var identity = new ClaimsIdentity(AuthDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        if (!string.IsNullOrEmpty(user.AvatarUrl)) identity.AddClaim(new Claim("avatar", user.AvatarUrl));

        context.Principal = new ClaimsPrincipal(identity);
        context.Properties ??= new AuthenticationProperties { RedirectUri = "/api/auth/complete" };

        await context.HttpContext.SignInAsync(AuthDefaults.AuthenticationScheme, context.Principal, context.Properties);
        return true;
    }
}

public static class AuthDefaults
{
    public const string AuthenticationScheme = "PublicView.Cookies";
}
