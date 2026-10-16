using Microsoft.EntityFrameworkCore;
using PublicView.Api.Models;

namespace PublicView.Api.Data;

public static class DbSeeder
{
    public static readonly (string Slug, string Name, string Icon, int Order)[] Categories =
    [
        ("jobs", "Jobs", "briefcase", 1),
        ("housing", "Housing", "home", 2),
        ("for-sale", "For Sale", "store", 3),
        ("services", "Services", "wrench", 4),
        ("gigs", "Gigs", "guitar", 5),
        ("resumes", "Resumes", "user-square", 6),
        ("community", "Community", "users", 7),
        ("personals-ads", "Personals", "heart", 8),
        ("events", "Events", "calendar", 9),
        ("lost-found", "Lost & Found", "magnifier", 10)
    ];

    public static async Task SeedAsync(AppDbContext db)
    {
        foreach (var (slug, name, icon, order) in Categories)
        {
            if (await db.Categories.AnyAsync(c => c.Slug == slug)) continue;
            db.Categories.Add(new Category
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Name = name,
                Icon = icon,
                SortOrder = order
            });
        }

        await db.SaveChangesAsync();

        if (!await db.Postings.AnyAsync(p => p.UserId == Guid.Empty))
        {
            var systemUser = await db.Users.FirstOrDefaultAsync(u => u.Id == Guid.Empty);
            if (systemUser is null)
            {
                systemUser = new User
                {
                    Id = Guid.Empty,
                    Email = "demo@publicview.local",
                    DisplayName = "PublicView Demo"
                };
                db.Users.Add(systemUser);
                await db.SaveChangesAsync();
            }

            var cats = await db.Categories.ToDictionaryAsync(c => c.Slug);
            var now = DateTimeOffset.UtcNow;
            var demo = new List<Posting>
            {
                new()
                {
                    Id = Guid.NewGuid(), UserId = systemUser.Id, Title = "Free desk (solid pine, must pick up today)",
                    Description = "Giving away a great solid pine desk. Tiny wobble on one leg (fixable). Must pick up before 6pm.",
                    Price = 0, City = "Seattle", Condition = "used", Status = PostingStatus.Published,
                    CreatedAt = now.AddHours(-1), ExpiresAt = now.AddDays(29), ContactInfo = "reply to this post"
                },
                new()
                {
                    Id = Guid.NewGuid(), UserId = systemUser.Id, Title = "Downtown 2BR apartment, parking included",
                    Description = "Sunny 2 bed / 1.5 bath near the waterfront. 850 sqft. In unit laundry, secure garage spot. Available Nov 1.",
                    Price = 1850, City = "Seattle", Condition = "new", Status = PostingStatus.Published,
                    CreatedAt = now.AddHours(-3), ExpiresAt = now.AddDays(29), ContactInfo = "mail@example.com"
                },
                new()
                {
                    Id = Guid.NewGuid(), UserId = systemUser.Id, Title = "Senior .NET engineer, remote (US)",
                    Description = "Fully-remote role building billing services on a modern .NET + Azure stack. Great team, minimal meetings.",
                    Price = 180000, City = "Remote", Condition = "new", Status = PostingStatus.Published,
                    CreatedAt = now.AddHours(-5), ExpiresAt = now.AddDays(29), ContactInfo = "hiring@example.com"
                }
            };

            demo[0].CategoryId = cats["for-sale"].Id;
            demo[1].CategoryId = cats["housing"].Id;
            demo[2].CategoryId = cats["jobs"].Id;
            db.Postings.AddRange(demo);
            await db.SaveChangesAsync();
        }
    }
}
