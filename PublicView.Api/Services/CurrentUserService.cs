using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Data;
using PublicView.Api.Models;

namespace PublicView.Api.Services;

public class CurrentUserService(AppDbContext db)
{
    public async Task<User?> GetCurrentUserAsync(HttpContext http)
    {
        var idStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? http.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(idStr, out var userId)) return null;
        return await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    }
}
